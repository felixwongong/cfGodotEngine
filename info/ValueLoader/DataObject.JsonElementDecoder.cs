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

            object jsonValue;
            if (jsonElement.ValueKind == JsonValueKind.Array)
            {
                var count = jsonElement.GetArrayLength();
                var array = new object[count];
                for (int i = 0; i < count; i++)
                {
                    array[i] = jsonElement[i];
                }

                jsonValue = array;
            }
            else
            {
                var value = jsonElement.ToObject();
                if (decodedType.IsArray)
                {
                    var array = new object[1];
                    array[0] = value;
                    jsonValue = array;
                } else
                {
                    jsonValue = value;
                }
            }

            return DataObject.Decoder.TryDecode(jsonValue, decodedType, out decoded);
        }
    }
}