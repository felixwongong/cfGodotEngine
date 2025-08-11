using System;
using System.Text.Json;
using cfEngine.Logging;
using cfEngine.Util;
using CofyDev.Xml.Doc;

namespace cfGodotEngine.Info
{
    public class JsonElementDecoder: DataObject.IValueDecoder
    {
        public Type valueType => typeof(System.Text.Json.JsonElement);
        public bool TryDecode(object raw, Type decodedType, out object decoded)
        {
            decoded = null;
            if (raw is not JsonElement jsonElement)
                return false;

            var jsonValue = jsonElement.ToObject();
            if (jsonValue == null)
            {
                Log.LogError($"Failed to deserialize JsonElement, jsonRawValue: {jsonElement.GetRawText()}");
                return false;
            }

            return DataObject.Decoder.TryDecode(jsonValue, decodedType, out decoded);
        }
    }
}