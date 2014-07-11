using Cirrious.CrossCore;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Mlearning.Core.Services.Entities;
using Referee.Core.Entities.azure;
using Referee.Core.Repositories;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AzureBlobUploader
{
    /// <summary>
    /// Esta clase permite subir recursos al servidor Azure alojado en la nube
    /// </summary>
    public class AzureUploader
    {



        //Register: Get SASQueryString (permissions) for Azure Uploading
        /// <summary>
        /// Este método registra un recurso para obtener un url y subirlo al servidor Azure
        /// </summary>
        /// <param name="filepath">Nombre de archivo que tendra el recurso en la nube</param>
        /// <returns>Objeto Resource con permisos para permitir la subida del recurso</returns>
        static async Task<Resource> registerResource(string filepath)
        {
            if (filepath == null)
            {
                filepath = getFileId();
            }

            Resource resource = new Resource { LocalPath = filepath, ContainerName = "rfresources" };
            IRepositoryService repo = new WAMSRepositoryService();

            await repo.InsertAsync<Resource>(resource);

            return resource;

        }

        //Register: Get SASQueryString (permissions) for Azure Uploading
        static async Task<Resource[]> registerResource(int nresources,List<string> filepaths)
        {
            Resource[] resourceList = new Resource[nresources];

            for (int i = 0; i < resourceList.Length; i++)
            {
                string filepath = getFileId();

                if (filepaths != null)
                    filepath = filepaths[i];

                resourceList[i] = new Resource { LocalPath = filepath, ContainerName = "rfresources" }; ;
            }

            if(nresources>0)
            {

                Resource r = await registerResource(resourceList[0].LocalPath);

                Uri uri = new Uri(r.CloudPath);
                var noLastSegment = string.Format("{0}://{1}", uri.Scheme, uri.Authority);

                for (int i = 0; i < uri.Segments.Length - 1; i++)
                {
                    noLastSegment += uri.Segments[i];
                }

                //Set CloudPath and SasQueryString for all resources
                for (int i = 0; i < resourceList.Length; i++)
                {                  

                    resourceList[i].SasQueryString = r.SasQueryString ;
                    resourceList[i].CloudPath = noLastSegment + resourceList[i].LocalPath;
                }
            }
            

             return resourceList;

        }
        /// <summary>
        /// Este método permite subir un conjunto de bytes al servidor ubicado en la nube
        /// </summary>
        /// <param name="resource">Objeto Resource que encapsula la url en el servidor</param>
        /// <param name="stream">Conjunto de bytes que se subirán</param>
        static  void uploadResource(Resource resource,Stream stream)
        {


            if (!string.IsNullOrEmpty(resource.SasQueryString))
            {
                // Get the URI generated that contains the SAS 
                // and extract the storage credentials.
                StorageCredentials cred = new StorageCredentials(resource.SasQueryString);
                var imageUri = new Uri(resource.CloudPath);

                // Instantiate a Blob store container based on the info in the returned item.
                CloudBlobContainer container = new CloudBlobContainer(new Uri(string.Format("https://{0}/{1}", imageUri.Host, resource.ContainerName)), cred);




                // Upload the new image as a BLOB from the stream.
                CloudBlockBlob blobFromSASCredential = container.GetBlockBlobReference(resource.LocalPath);

                
                blobFromSASCredential.UploadFromStream(stream);
                
                Mvx.Trace("Uploaded ");




                // When you request an SAS at the container-level instead of the blob-level,
                // you are able to upload multiple streams using the same container credentials.
            }
            else
            {
                Mvx.Trace("No SAS QUERY ");

            }
         
        }

        /// <summary>
        /// Este método permite subir un archivo al servidor en la nube
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="filepath"></param>
        /// <returns>La Url que identifica el recurso en internet </returns>
        public static async Task<string> uploadFile(Stream stream,string filepath)
        {
          
            string cloudPath = String.Empty;
            try
            {

                Resource r = await registerResource(filepath);
                cloudPath = r.CloudPath;
                uploadResource(r, stream);
            }
            catch(WebException e)
            {
                Mvx.Trace("Error: "+e.Message);
                throw;
            }

            return cloudPath;
        }

        /// <summary>
        /// Método que permite subir multiples recursos a la vez al servidor en la nube
        /// </summary>
        /// <param name="streamList">Lista de bytes que se subirán</param>
        /// <param name="filepaths">Nombre de los archivos que se subirán</param>
        /// <returns>Una lista de URLs identificando cada recurso en internet</returns>
        public static async Task<List<string>> uploadMultipleFiles(List<Stream> streamList,List<string> filepaths)
        {

            List<string> result = new List<string>();

            try
            {
                Resource [] resources = await registerResource(streamList.Count, filepaths);

                for (int i = 0; i < resources.Length; i++)
                {
                    result.Add(resources[i].CloudPath);
                }

                for (int i = 0; i < streamList.Count; i++)
                {
                    uploadResource(resources[i], streamList[i]);
                }
            }
            catch (WebException e)
            {
                Mvx.Trace("Error " + e.Message);
                throw;
            }

           return result;
        }

        /// <summary>
        /// Método usado para obtener un identificador único
        /// </summary>
        /// <returns>Una cadena de caracteres como identificador de archivo</returns>
        static string getFileId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
