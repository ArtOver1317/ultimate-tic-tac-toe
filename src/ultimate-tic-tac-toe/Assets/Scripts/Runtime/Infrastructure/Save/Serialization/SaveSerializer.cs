using System;
using SimpleJSON;
using UnityEngine;
using JsonNode = SimpleJSON.JSONNode;

namespace Runtime.Infrastructure.Save.Serialization
{
    internal sealed class SaveSerializer
    {
        private const string _jsonNullLiteral = "null";

        public string Serialize<T>(T data)
        {
            if (data == null)
                return _jsonNullLiteral;

            var type = typeof(T);

            if (type == typeof(string))
                return new JSONString((string)(object)data).ToString();

            if (type == typeof(int))
                return new JSONNumber((int)(object)data).ToString();

            if (type == typeof(bool))
                return new JSONBool((bool)(object)data).ToString();

            return type.IsArray ? SerializeArray((Array)(object)data) : JsonUtility.ToJson(data);
        }

        public bool TryDeserialize<T>(string json, out T value)
        {
            var type = typeof(T);

            if (type == typeof(string))
                return TryDeserializeString(json, out value);

            if (type == typeof(int))
                return TryDeserializeInt(json, out value);

            if (type == typeof(bool))
                return TryDeserializeBool(json, out value);

            return type.IsArray 
                ? TryDeserializeArrayValue(type, json, out value) 
                : TryDeserializeObject(json, out value);
        }

        private static string SerializeArray(Array array)
        {
            var jsonArray = new JSONArray();

            for (var i = 0; i < array.Length; i++)
            {
                var item = array.GetValue(i);
                
                if (item == null)
                {
                    jsonArray.Add(JSONNull.CreateOrGet());
                    continue;
                }

                jsonArray.Add(JsonNode.Parse(JsonUtility.ToJson(item)));
            }

            return jsonArray.ToString();
        }

        private static bool TryDeserializeArray(Type arrayType, string json, out object arrayValue)
        {
            arrayValue = null;

            var parsed = JsonNode.Parse(json);
            
            if (parsed is not JSONArray jsonArray)
                return false;

            var elementType = arrayType.GetElementType();
            
            if (elementType == null)
                return false;

            var result = Array.CreateInstance(elementType, jsonArray.Count);
            
            for (var i = 0; i < jsonArray.Count; i++)
            {
                var node = jsonArray[i];
                
                if (node == null || node.IsNull)
                    continue;

                var item = JsonUtility.FromJson(node.ToString(), elementType);
                
                if (item == null)
                    return false;

                result.SetValue(item, i);
            }

            arrayValue = result;
            return true;
        }

        private static bool TryDeserializeString<T>(string json, out T value)
        {
            if (!TryParseNode(json, out var parsed) || parsed is not JSONString jsonString)
            {
                value = default;
                return false;
            }

            value = (T)(object)jsonString.Value;
            return true;
        }

        private static bool TryDeserializeInt<T>(string json, out T value)
        {
            if (!TryParseNode(json, out var parsed) || parsed is not JSONNumber jsonNumber)
            {
                value = default;
                return false;
            }

            value = (T)(object)jsonNumber.AsInt;
            return true;
        }

        private static bool TryDeserializeBool<T>(string json, out T value)
        {
            if (!TryParseNode(json, out var parsed) || parsed is not JSONBool jsonBool)
            {
                value = default;
                return false;
            }

            value = (T)(object)jsonBool.AsBool;
            return true;
        }

        private static bool TryDeserializeArrayValue<T>(Type arrayType, string json, out T value)
        {
            if (!TryDeserializeArray(arrayType, json, out var arrayValue))
            {
                value = default;
                return false;
            }

            value = (T)arrayValue;
            return true;
        }

        private static bool TryDeserializeObject<T>(string json, out T value)
        {
            if (string.Equals(json, _jsonNullLiteral, StringComparison.OrdinalIgnoreCase))
            {
                value = default;
                return false;
            }

            var deserialized = JsonUtility.FromJson<T>(json);
            
            if (deserialized == null)
            {
                value = default;
                return false;
            }

            value = deserialized;
            return true;
        }

        private static bool TryParseNode(string json, out JsonNode parsed)
        {
            parsed = JsonNode.Parse(json);
            return parsed != null;
        }
    }
}