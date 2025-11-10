using System;
namespace AzureCDNImageResizer.Options
{
    public class ClientCacheOptions
    {
        public TimeSpan MaxAge { get; set; }  = TimeSpan.FromDays(5);
    }
}