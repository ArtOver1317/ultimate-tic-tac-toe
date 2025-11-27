using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.Core
{
    public static class UxmlBinder
    {
        private static readonly Dictionary<Type, FieldBindingInfo[]> _bindingCache = new();
        
        private static readonly MethodInfo _queryMethod = typeof(UxmlBinder).GetMethod(
            nameof(QueryElement), 
            BindingFlags.NonPublic | BindingFlags.Static
        );

        private class FieldBindingInfo
        {
            public FieldInfo Field;
            public string ElementName;
            public bool IsOptional;
            public MethodInfo GenericQueryMethod;
        }

        public static void BindElements(object target, VisualElement root)
        {
            if (target == null || root == null)
            {
                Debug.LogError("[UxmlBinder] Target or root is null!");
                return;
            }

            var type = target.GetType();
            var bindings = GetOrCreateBindings(type);

            foreach (var binding in bindings)
            {
                var element = binding.GenericQueryMethod.Invoke(null, new object[] { root, binding.ElementName });

                if (element == null)
                {
                    if (!binding.IsOptional) 
                        Debug.LogError($"[UxmlBinder] Required element '{binding.ElementName}' of type {binding.Field.FieldType.Name} not found in UXML for field {binding.Field.Name} in {type.Name}!");
                }
                else
                    binding.Field.SetValue(target, element);
            }
        }

        private static FieldBindingInfo[] GetOrCreateBindings(Type type)
        {
            if (_bindingCache.TryGetValue(type, out var cached))
                return cached;

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var bindings = new List<FieldBindingInfo>();

            foreach (var field in fields)
            {
                var attribute = field.GetCustomAttribute<UxmlElementAttribute>();
                
                if (attribute == null)
                    continue;

                if (!typeof(VisualElement).IsAssignableFrom(field.FieldType))
                {
                    Debug.LogWarning($"[UxmlBinder] Field {field.Name} in {type.Name} has UxmlElement attribute but is not a VisualElement type!");
                    continue;
                }

                var elementName = attribute.Name ?? GetFieldNameWithoutPrefix(field.Name);
                var genericMethod = _queryMethod.MakeGenericMethod(field.FieldType);

                bindings.Add(new FieldBindingInfo
                {
                    Field = field,
                    ElementName = elementName,
                    IsOptional = attribute.IsOptional,
                    GenericQueryMethod = genericMethod,
                });
            }

            var result = bindings.ToArray();
            _bindingCache[type] = result;
            return result;
        }

        private static T QueryElement<T>(VisualElement root, string name) where T : VisualElement => 
            root.Q<T>(name);

        private static string GetFieldNameWithoutPrefix(string fieldName)
        {
            if (fieldName.StartsWith("_"))
                fieldName = fieldName[1..];
            
            if (fieldName.Length > 0)
                fieldName = char.ToUpper(fieldName[0]) + fieldName[1..];
            
            return fieldName;
        }

#if UNITY_EDITOR
        public static void ValidateBindings(object target, VisualElement root)
        {
            if (target == null || root == null)
                return;

            var type = target.GetType();
            var bindings = GetOrCreateBindings(type);
            var missingElements = new List<string>();

            foreach (var binding in bindings)
            {
                var element = binding.GenericQueryMethod.Invoke(null, new object[] { root, binding.ElementName });
                
                if (element == null && !binding.IsOptional) 
                    missingElements.Add($"- {binding.ElementName} ({binding.Field.FieldType.Name}) for field '{binding.Field.Name}'");
            }

            if (missingElements.Count > 0) 
                Debug.LogWarning($"[UxmlBinder] Missing elements in {type.Name}:\n{string.Join("\n", missingElements)}");
        }
#endif
    }
}

