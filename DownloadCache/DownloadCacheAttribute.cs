using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.DownloadCache
{
    /// <summary>
    /// Clase que permite identificar una propiedad de otra clase como un URL de un recurso 
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]     
    public class DownloadCacheAttribute : System.Attribute
    {
        public override string ToString()
        {
            return "DownloadCacheable";
        }
    }   
}
