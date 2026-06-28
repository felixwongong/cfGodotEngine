using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using cfEngine.Info;
using cfEngine.IO;
using cfEngine;
using cfEngine.Pooling;
using CofyDev.Xml.Doc;
using JsonSerializer = cfEngine.Serialize.JsonSerializer;

namespace cfGodotEngine.Info
{
    public class ExcelJsonLoader<TInfo> : IValueLoader<TInfo>
    {
        private readonly IStorage _storage;
        private readonly DataRowMapper _mapper;
        
        private const string SEARCH_PATTERN = "*.json";
        
        public ExcelJsonLoader(IStorage storage, DataRowMapper mapper)
        {
            _storage = storage;
            _mapper = mapper;
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

            var excelData = new DataTable();
            foreach (var file in files)
            {
                var fileByte = _storage.LoadBytes(file);
                var fileExcelData = json.DeserializeAs<DataTable>(fileByte);
                excelData.AddRange(fileExcelData);
            }

            if (_mapper == null)
            {
                throw new ArgumentNullException(nameof(_mapper), "mapper unset");
            }

            var handle = ListPool<TInfo>.Default.Get(out values);
            values.EnsureCapacity(excelData.Count);
            foreach (var dataRow in excelData)
            {
                var decoded = _mapper.DecodeAs<TInfo>(dataRow, DataRowExtension.SetPropertyValue);
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
                var excelData = new DataTable();
                var json = new JsonSerializer.Builder().Build();
                foreach (var bytes in task.Result)
                {
                    var fileExcelData = json.DeserializeAs<DataTable>(bytes);
                    excelData.AddRange(fileExcelData);
                }

                if (_mapper == null)
                {
                    throw new ArgumentNullException(nameof(_mapper), "mapper unset");
                }

                var values = new List<TInfo>(excelData.Count);
                foreach (var dataRow in excelData)
                {
                    var decoded = _mapper.DecodeAs<TInfo>(dataRow, DataRowExtension.SetPropertyValue);
                    values.Add(decoded);
                }

                return values;
            }, cancellationToken);
        }
    }
}