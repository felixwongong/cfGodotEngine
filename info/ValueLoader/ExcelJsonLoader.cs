using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using cfEngine.Info;
using cfEngine.IO;
using cfEngine.Logging;
using cfEngine.Pooling;
using CofyDev.Xml.Doc;
using JsonSerializer = cfEngine.Serialize.JsonSerializer;

namespace cfGodotEngine.Info
{
    public class ExcelJsonLoader<TInfo> : IValueLoader<TInfo>
    {
        private readonly IStorage _storage;
        private readonly DataObjectEncoder _encoder;
        
        private const string SEARCH_PATTERN = "*.json";
        
        public ExcelJsonLoader(IStorage storage, DataObjectEncoder encoder)
        {
            _storage = storage;
            _encoder = encoder;
        }

        public ListPool<TInfo>.Handle Load(out List<TInfo> values)
        {
            var json = new JsonSerializer.Builder().Build();
            
            var files = _storage.GetFiles(SEARCH_PATTERN);
            if (files.Length <= 0)
            {
                Log.LogInfo($"{typeof(TInfo)} infoCount: 0");
                values = new List<TInfo>(0);
                return ListPool<TInfo>.Handle.Empty;
            }

            var excelData = new DataContainer();
            foreach (var file in files)
            {
                var fileByte = _storage.LoadBytes(file);
                var fileExcelData = json.DeserializeAs<DataContainer>(fileByte);
                excelData.AddRange(fileExcelData);
            }

            if (_encoder == null)
            {
                throw new ArgumentNullException(nameof(_encoder), "encoder unset");
            }

            var handle = ListPool<TInfo>.Default.Get(out values);
            values.EnsureCapacity(excelData.Count);
            foreach (var dataObject in excelData)
            {
                var decoded = _encoder.DecodeAs<TInfo>(dataObject, DataObjectExtension.SetDecodePropertyValue);
                values.Add(decoded);
            }
            
            return handle;
        }

        public Task<List<TInfo>> LoadAsync(CancellationToken cancellationToken)
        {
            var files = _storage.GetFiles(SEARCH_PATTERN);
            if (files.Length <= 0)
            {
                Log.LogWarning("serialized file ({infoDirectory}) not found in Info Directory, please check the file name and path.");
                return Task.FromResult<List<TInfo>>(new List<TInfo>(0));
            }
            
            using var handle = ListPool<Task<byte[]>>.Default.Get(out var byteLoadTasks);
            byteLoadTasks.EnsureCapacity(files.Length);
            
            foreach (var file in files)
            {
                byteLoadTasks.Add(_storage.LoadBytesAsync(file, cancellationToken));
            }

            var byteLoadResult = Task.WhenAll(byteLoadTasks);
            return byteLoadResult.ContinueWith(task =>
            {
                var excelData = new DataContainer();
                foreach (var bytes in task.Result)
                {
                    var fileExcelData = CofyXmlDocParser.ParseExcel(bytes);
                    excelData.AddRange(fileExcelData);
                }

                if (_encoder == null)
                {
                    throw new ArgumentNullException(nameof(_encoder), "encoder unset");
                }

                var values = new List<TInfo>(excelData.Count);
                foreach (var dataObject in excelData)
                {
                    var decoded = _encoder.DecodeAs<TInfo>(dataObject, DataObjectExtension.SetDecodePropertyValue);
                    values.Add(decoded);
                }

                return values;
            }, cancellationToken);
        }
    }
}