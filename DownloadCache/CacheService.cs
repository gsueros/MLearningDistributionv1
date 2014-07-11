using Cirrious.CrossCore;
using Cirrious.MvvmCross.Plugins.File;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;
using Referee.Core.Session;

namespace Core.DownloadCache
{
    /// <summary>
    /// Esta clase permite la descarga de recursos y su almacenamiento para un uso posterior sin conexión de datos
    /// </summary>
    public class CacheService
    {

        

        //Needs MvxFile Plugin
        /// <summary>
        /// Esta clase permite registrar la metadata de un recurso para su almacenamiento en disco
        /// </summary>
        public class Entry
        {
            public string HttpSource { get; set; }
            public string DownloadedPath { get; set; }
            public DateTime WhenLastAccessedUtc { get; set; }
            public DateTime WhenDownloadedUtc { get; set; }
        }


        /// <summary>
        /// Nombre  del fichero que sirve como índice para la búsqueda de recursos en disco
        /// </summary>
        string _indexFileName = "_cacheindex.txt";      
        Dictionary<string, Entry> _indexByHttp;
        static CacheService _instance = null;
        List<string> _persistentFiles;

        static string _entityPrefix = "kf_id_";

        TimeSpan _maxFileAge = TimeSpan.FromDays(3.0);



        IMvxFileStore storage;
        /// <summary>
        /// Constructor que inicializa las propiedades y limpia archivos no indexados
        /// </summary>
        private CacheService()
        {
         
            storage = Mvx.Resolve<IMvxFileStore>();
            _indexByHttp = new Dictionary<string, Entry>();
            _persistentFiles = new List<string>();

            //Add Files that won't be erased by the CacheCleaner
            _persistentFiles.Add(_indexFileName);

            loadEntries();
            checkForUnindexedFiles();
            checkForOldFiles();
            writeEntries();
           
        }

        private CacheService(string credFileName) 
        {
            storage = Mvx.Resolve<IMvxFileStore>();
            _indexByHttp = new Dictionary<string, Entry>();
            _persistentFiles = new List<string>();

            //Add Files that won't be erased by the CacheCleaner
            _persistentFiles.Add(_indexFileName);
            _persistentFiles.Add(credFileName);

            loadEntries();
            checkForUnindexedFiles();
            checkForOldFiles();
            writeEntries();
           
        }


        /// <summary>
        /// Método que permite la limpieza de recursos muy antiguos en almacenamiento secundario
        /// </summary>
        private void checkForOldFiles()
        {
            var now = DateTime.UtcNow;
            var toRemove = _indexByHttp.Values.Where(x => (now - x.WhenDownloadedUtc) > _maxFileAge).ToList();

            foreach (var item in toRemove)
            { 
                _indexByHttp.Remove(item.HttpSource)   ;
            }

            deleteFile(toRemove.Select(x => x.DownloadedPath));
            
        }

        /// <summary>
        /// Método que realiza la eliminación de un archivo por su nombre
        /// </summary>
        /// <param name="name">Nombre del archivo a eliminar</param>
        private void deleteFile(string name)
        {
            storage.DeleteFile(name);
        }

        /// <summary>
        /// Método que realiza la eliminación de múltiples archivos 
        /// </summary>
        /// <param name="names">Lista de nombres de los arhivos a eliminar</param>
        private void deleteFile(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                deleteFile(name);
            }
        }

        /// <summary>
        /// Método que elimina cualquier recurso, en la carpeta raiz de la aplicación, que no esté indexado 
        /// </summary>
        private void checkForUnindexedFiles()
        {
            var files = storage.GetFilesIn("");
            List<string> filepaths = new List<string>();
            string npath = storage.NativePath("");
            npath += "/";
            //Standarization ---------GETFILESIN not reliable
            foreach (var f in files)
            { 
               
                //Removing native path 
                int index = f.IndexOf(npath);
              string cleanPath = (index < 0)
                ? f
                : f.Remove(index, npath.Length);
              filepaths.Add(cleanPath);
            }
           
            var cachedFiles = new Dictionary<string, Entry>();

            foreach (var entry in _indexByHttp)
            {
                cachedFiles[entry.Value.DownloadedPath] = entry.Value;
            }

            foreach (var file in filepaths)
            {

                bool endsWith = false;
                foreach (var pfilename in _persistentFiles)
                {
                    if (file.EndsWith(pfilename))
                        endsWith = true;
                }
                if (!cachedFiles.ContainsKey(file) && !endsWith)
                {
                    deleteFile(file);
                }
            }

        }

        public static CacheService Init()
        {
            if (_instance == null)
                _instance = new CacheService();

            return _instance;
        }

        public static CacheService Init(string credentialFilename)
        {
            if (_instance == null)
                _instance = new CacheService(credentialFilename);

            return _instance;
        }

        /// <summary>
        /// Método que permite la carga del archivo índice de disco a memoria
        /// </summary>
        private void loadEntries()
        {


            if (storage.Exists(_indexFileName))
            {
                // Load It

                string contents;
                if (storage.TryReadTextFile(_indexFileName, out  contents))
                {
                    var list = JsonConvert.DeserializeObject<List<Entry>>(contents);
                    _indexByHttp = list.ToDictionary(x => x.HttpSource, x => x);
                }

            }
          

        }

        /// <summary>
        /// Método que permite guardar el archivo índice en disco
        /// </summary>
        private void writeEntries()
        {

            List<Entry> toSave;

            toSave = _indexByHttp.Values.ToList();

            var text = JsonConvert.SerializeObject(toSave);

            storage.WriteFile(_indexFileName, text); ;

           

        }


        /// <summary>
        /// Método que permite guardar en cache las propiedades de un objeto marcadas con el atributo DownloadCacheAttribute
        /// </summary>
        /// <param name="obj">Objeto cuyas propiedades serán almacenadas en cache</param>
        /// <returns>Diccionario con el nombre de la propiedad como clave y los bytes del recurso como valor</returns>
        public async Task<Dictionary<string, byte[]>> cachePropertiesFromObject(object obj)
        {
            var t = obj.GetType().GetTypeInfo();
            var properties = t.DeclaredProperties;

           var dnAttributes =  properties.Where(pi => pi.GetCustomAttributes(typeof(DownloadCacheAttribute), false).Count()> 0);

            //Indexed by property name
           Dictionary<string, byte[]> result = new Dictionary<string, byte[]>();
           foreach (var propInfo in dnAttributes)
           {
               string urlValue = propInfo.GetValue(obj) as string;

               Debug.WriteLine("Property Value: " + urlValue);

               var request = (HttpWebRequest)WebRequest.Create(urlValue);
               request.Method = "GET";
            //   request.Accept = "image/*";
               await makeRequest(request, (stream) => 
               {
                   MemoryStream ss = new MemoryStream();
                   stream.CopyToAsync(ss);
                   result.Add(propInfo.Name, ss.ToArray()); 
               }
                                                        //True : We want to cache it
               , (error) => { Debug.WriteLine("Error writing file inside cachePropertiesFromObject"); }, true);
           }

           return result;
            
        
        }

        /// <summary>
        /// Método que guarda almacena los bytes de un recurso en disco
        /// </summary>
        /// <param name="url">URL del recurso que sirve como su identificador </param>
        /// <param name="bytes">Bytes del recurso a almacenar</param>
        public   void cacheResource(string url,byte[] bytes)
        {

                  loadEntries();


              

               

                Entry entry = new Entry {  HttpSource = url, WhenDownloadedUtc = DateTime.UtcNow, WhenLastAccessedUtc = DateTime.UtcNow };

                Entry old;
                string filename;

                if (_indexByHttp.TryGetValue(url, out old))
                {
                    //If exist , ovewrite it
                    filename = old.DownloadedPath;

                    entry.DownloadedPath = filename;
                    _indexByHttp[url] = entry;
              
                }
                else
                {
                    //Doesn't exist

                    //Get new FileName            
                    filename = Guid.NewGuid().ToString("N");

                    entry.DownloadedPath = filename;
                    //  add it
                    _indexByHttp.Add(entry.HttpSource, entry);    
                }

             //   FileService.writeStreamAsync(stream, filename);
                storage.WriteFile(filename, bytes);
                

                writeEntries();

            
           
        }


        /// <summary>
        /// Método que permite almacenar una lista de objetos en disco
        /// </summary>
        /// <typeparam name="T">Tipo del objeto a ser almacenado</typeparam>
        /// <param name="identifier">Identificador del archivo que contendra la información de la lista de objetos</param>
        /// <param name="objList">Lista de objetos a almacenar</param>
        public void cacheObjectList<T>(string identifier,List<T> objList)
        {

            
            string json = JsonConvert.SerializeObject(objList);
            byte[] bytes = GetBytes(json);

            cacheResource(_entityPrefix+ identifier,bytes);
         

        }

        public T deserializeObjectListFromBytes<T>(byte[] bytes)
        {
            string json = GetString(bytes);

            T result =  JsonConvert.DeserializeObject<T>(json);

            return result;
        }


        /// <summary>
        /// Método que permite la carga de un recurso almacenado en cache dado su url
        /// </summary>
        /// <param name="url">El URL utilizado para identificar el recurso en disco</param>
        /// <param name="ok">TRUE: si la carga fue existosa, FALSE de lo contrario</param>
        /// <returns>Los bytes del recurso cargado de disco</returns>
        public  byte[] tryReadFileFromLocalCache(string url, out bool ok) 
        {
            loadEntries();


            Entry entry;
            ok = _indexByHttp.TryGetValue(url,out entry);


            try
            {
                //Update last accessed date
                entry.WhenLastAccessedUtc = DateTime.UtcNow;

                byte[] content;                
                storage.TryReadBinaryFile(entry.DownloadedPath, out content);

                //Write modified entries
                writeEntries();

                

                return content;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                return null;
            }



        }


        //Cache: determines if the resource is going to be cached or not.
        //If there's no connection, try to read it from cache
        /// <summary>
        /// Método que hace una petición GET a la dirección dada
        /// </summary>
        /// <param name="request">Objeto que encapsula la dirección del servidor al cual se hará la petición</param>
        /// <param name="successAction">Acción a realizar si la petición fue exitosa</param>
        /// <param name="errorAction">Acción a ejecutar si la petición fue errónea</param>
        /// <param name="cache">TRUE si se desea almacenar en cache la respuesta de la petición </param>
        /// <returns></returns>
        public async Task makeRequest(HttpWebRequest request, Action<Stream> successAction, Action<Exception> errorAction,bool cache)
        {
            
          

            try
            {
                
                using (var wresponse = await Task<WebResponse>.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, request))
                {
                    
                    using (var stream = wresponse.GetResponseStream())
                    {

                        //Using memory stream to reset Position and use the stream twice
                        MemoryStream ss = new MemoryStream();
                        await stream.CopyToAsync(ss);

                        // Write to Local Cache 

                        if (cache)
                        {
                           

                            byte[] bytes = ss.ToArray();
                            cacheResource(request.RequestUri.ToString(), bytes);
                            
                        }

                        ss.Position = 0;




                        successAction(ss);

                    }
                }
            }
            catch (WebException ex)
            {
                //Using Cache  when No connection
                Debug.WriteLine( "No connection");
                bool ok;
                var content = tryReadFileFromLocalCache(request.RequestUri.ToString(), out ok);

                if (ok)
                {
                    successAction(new MemoryStream(content));
                }
                else
                {
                    Mvx.Error("ERROR: '{0}' when making {1} request to {2}", ex.Message, request.Method, request.RequestUri.AbsoluteUri);
                    errorAction(ex);
                    throw ex;
                    
                }



            }

         
        }

        static byte[] GetBytes(string str)
        {
            byte[] bytes = new byte[str.Length * sizeof(char)];
            System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            return bytes;
        }


        public static string GetStringWithPrefix(string identifier)
        {
            return _entityPrefix + identifier;
        }

         static string GetString(byte[] bytes)
        {
            char[] chars = new char[bytes.Length / sizeof(char)];
            System.Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
            return new string(chars);
        }


        /// <summary>
        /// Método que hace una petición GET a un servidor y obtiene JSON como respuesta   
        /// </summary>
        /// <param name="requestUrl">Url del servidor al cuál se hará la petición</param>
        /// <returns></returns>
         public async Task<string> GetJson(string requestUrl)
         {
             
            
             var request = (HttpWebRequest)WebRequest.Create(requestUrl);
             CacheService service = CacheService.Init(SessionService.GetCredentialFileName());
             string jsonResult = String.Empty;
             await service.makeRequest(request,
                 (stream) =>
                 {
                     
                     StreamReader reader = new StreamReader(stream);
                     jsonResult = reader.ReadToEnd();
                    
                 },
                 (error) =>
                 {
                     throw (error);

                 },
                 false
             );

             return jsonResult;
         }








    }
}
