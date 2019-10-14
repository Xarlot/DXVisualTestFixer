using DXVisualTestFixer.Common;
using Minio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;
using Minio.DataModel;

namespace DXVisualTestFixer.Core {
    public class MinioWorker : IMinioWorker {
        static MinioClient minio = new MinioClient("anallytics-minio:9000", "xpfminio", "xpfminiostorage");

        public async Task<string> Download(string path) {
            try {
                string result = null;
                await minio.GetObjectAsync("visualtests", path, stream => {
                    using(var reader = new StreamReader(stream)) {
                        result = reader.ReadToEnd();
                    }
                });
                return result;
            }
            catch {
                return null;
            }
        }
        public async Task<string[]> Discover(string path) {
            try {
                List<string> result = new List<string>();
                IObservable<Item> observable = minio.ListObjectsAsync("visualtests", path, false);
                IDisposable subscription = observable.Subscribe
                (
                    item => result.Add(item.Key),
                    ex => throw ex
                );
                await observable.ToTask();
                subscription.Dispose();
                return result.ToArray();
            }
            catch {
                return null;
            }
        }
    }
}
