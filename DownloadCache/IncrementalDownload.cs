using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using   System.Collections.ObjectModel;
using System.IO;

using System.Net;

using Cirrious.CrossCore;
using Core.DownloadCache;
using Referee.Core.Session;







namespace Core.Classes
{
    /// <summary>
    /// Esta clase permite la descarga incremental de una lista de recursos
    /// </summary>
    public class IncrementalDownload
    {

        /// <summary>
        /// Cantidad de recursos descargados
        /// </summary>
        public int CurrentSize{get;set;}

        /// <summary>
        /// Tamaño del siguiente bloque de recursos a descargar
        /// </summary>
        int _next_block_tam;
        //Limits when to download the nextblock        
        int _division_factor;

        public IncrementalDownload()
        {
            CurrentSize = 0;
            _next_block_tam = 10;
            _division_factor = 2;
        }

      
        /// <summary>
        /// Método utilizado para descargar los bytes de un recurso identificado por una url
        /// </summary>
        /// <typeparam name="T">Tipo de objetos</typeparam>
        /// <param name="objs">Lista de objetos que tienen una url como propiedad</param>
        /// <param name="getUrl">Función que obtiene la url a partir de un objeto de tipo T</param>
        /// <returns></returns>
        public async Task <List<Tuple<int,byte[]>>> FillStream<T>( List<T> objs,Func<T,string> getUrl)
        {

            List<Tuple<int,byte[]>> toReturn = new List<Tuple<int,byte[]>>();

          
            for (int i = CurrentSize; i < CurrentSize + _next_block_tam; i++)
            {
                if (i < objs.Count)
                {
                   
                 
                        var bytes = await LoadBytes(objs[i], getUrl);
                        toReturn.Add(new Tuple<int, byte[]>(i, bytes));
                  
                   
                }
            }

            CurrentSize += _next_block_tam;


            return toReturn;
        }

        /// <summary>
        /// Método que permite cargar los bytes de un recurso en una propiedad del objeto tipo T
        /// </summary>
        /// <typeparam name="T">Tipo del objeto</typeparam>
        /// <param name="index">Indica la posición del objeto que se muestra </param>
        /// <param name="toLoadList">Lista de objetos donde se cargaran los bytes</param>
        /// <param name="loadAction">Acción que permite setear los bytes al objeto en la posición dada</param>
        /// <param name="getUrl">Función que obtiene la url del recurso encapsulado en el objeto de tipo T</param>
        /// <returns></returns>
        public async Task TryLoadByteVector<T>(int index, List<T> toLoadList, Action<int, byte[]> loadAction, Func<T, string> getUrl)
        {
            if (index >= CurrentSize / _division_factor)
            {
                //Download the next block  and add them to the Property
                var list = await FillStream<T>(toLoadList,getUrl);

                foreach (var item in list)
                {                 
                    //LocalsResult[item.Item1].image_bytes = bytes;
                    loadAction(item.Item1, item.Item2);
                }


            }
        }

      
        /// <summary>
        /// Método que permite obtener los bytes en memoria de un recurso identificado por una URL encapsulada en un objeto de tipo T
        /// </summary>
        /// <typeparam name="T">Tipo del objeto</typeparam>
        /// <param name="toLoadObj">Objeto que encapusla en una propiedead la url del recurso en mención</param>
        /// <param name="getUrl">Función que obtiene la url del recurso dado un objeto de tipo T</param>
        /// <returns>Retorna los bytes en memoria que representan el recurso</returns>
        public async Task<byte[]> LoadBytes<T>(T toLoadObj, Func<T, string> getUrl)
        {
                byte[] toReturn = null;
                CacheService cache = CacheService.Init(SessionService.GetCredentialFileName());
                try
                {
                      
                    var request = (HttpWebRequest)WebRequest.Create(getUrl(toLoadObj));
                    request.Method = "GET";
                    await cache.makeRequest(request, stream => { BinaryReader r = new BinaryReader(stream); toReturn =  r.ReadBytes((int)stream.Length); }, error => { throw error; }, true);
                }
                catch(Exception ex)
                {
                    Mvx.Trace("Resource not found");
                    
                }

                return toReturn;
       }
        
    }

    
        




}
