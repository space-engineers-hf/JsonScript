using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;
using System.Collections.Immutable;

namespace IngameScript
{
    partial class Program
    {
        /// <summary>
        /// Provides functionality (very light) to serialize objects or value types to JSON and to deserialize JSON into objects or value types.
        /// </summary>
        public static class JsonParser
        {

            /// <summary>
            /// Parses the text representing a single JSON value into an instance of the type specified by a generic type parameter.
            /// </summary>
            /// <typeparam name="TValue">The type to deserialize the JSON value into.</typeparam>
            /// <param name="json">The JSON text to parse.</param>
            /// <returns>A <typeparamref name="TValue"/> representation of the JSON value.</returns>
            /// <exception cref="FormatException">Some text has wrong format or it is not recognized because the type is not implemented for serializing.</exception>
            public static TValue Deserialize<TValue>(string json, JsonParserOptions options)
            {
                return (TValue)Deserialize(json, typeof(TValue), options);
            }

            /// <summary>
            /// Parses the text representing a single JSON value into a <paramref name="returnType"/>.
            /// </summary>
            /// <param name="json">The JSON text to parse.</param>
            /// <param name="returnType">The type of the object to convert to and return.</param>
            /// <returns>A <paramref name="returnType"/> representation of the JSON value.</returns>
            /// <exception cref="FormatException">Some text has wrong format or it is not recognized because the type is not implemented for serializing.</exception>
            /// <exception cref="JsonParseException">Some text has wrong format or it is not recognized because the type is not implemented for serializing.</exception>
            public static object Deserialize(string json, Type returnType, JsonParserOptions options)
            {
                return DeserializeInternal(json, returnType, options);
            }

            public static object DeserializeInternal(string json, Type returnType, JsonParserOptions options)
            {
                object result;
                var regexString = CreateRegex(returnType, options, firstLevel: true);
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(json, regexString);

                if (jsonMatch.Success)
                {
                    result = DeserializeInternal(jsonMatch, returnType, options);
                }
                else
                {
                    throw new FormatException();
                }
                return result;
            }

            private static object DeserializeInternal(System.Text.RegularExpressions.Match jsonMatch, Type returnType, JsonParserOptions options)
            {
                object result;

                if (returnType == typeof(DateTime))
                {
                    result = DateTime.Parse(jsonMatch.Groups["content"].Value);
                }
                else if (returnType == typeof(string))
                {
                    result = jsonMatch.Groups["content"].Value;
                }
                else if (IsNumericType(returnType))
                {
                    result = ParseNumber(returnType, jsonMatch.Value);
                }
                else if (options.IsEnumerable(returnType))
                {
                    var genericType = options.GetGenericType(returnType);
                    var pattern = CreateRegex(genericType, options);
                    var submatches = System.Text.RegularExpressions.Regex.Matches(jsonMatch.Groups["content"].Value, pattern).OfType<System.Text.RegularExpressions.Match>();

                    result = options.CreateInstanceList(genericType);
                    foreach (var submatch in submatches)
                    {
                        var item = DeserializeInternal(submatch, genericType, options);

                        ((IList)result).Add(item);
                    }
                }
                else
                {
                    result = options.CreateInstance(returnType);
                    foreach (var property in options.GetProperties(returnType))
                    {
                        var value = DeserializeInternal(jsonMatch.Groups[property.Name].Value, property.PropertyType, options);

                        property.SetValue(result, value);
                    }
                }
                return result;
            }

            private static string CreateRegex(Type returnType, JsonParserOptions options, bool firstLevel = true)
            {
                string result;
                IEnumerable<dynamic> properties;

                if (returnType == typeof(string) || returnType == typeof(DateTime))
                {
                    result = $@"\""({(firstLevel ? "?<content>" : "?:")}(?:\\\""|[^\""])*)\""";
                }
                else if (IsNumericType(returnType))
                {
                    result = @"(?:\-?\d+\.?\d*)";
                }
                else if (options.IsEnumerable(returnType))
                {
                    var genericType = options.GetGenericType(returnType);

                    result = $@"\[(?{(firstLevel ? "<content>" : ":")}(?:(?:{CreateRegex(genericType, options, firstLevel: false)})\,?)*)\]";
                }
                else if ((properties = options.GetProperties(returnType)).Any())
                {
                    var builder = new List<string>();

                    foreach (var property in properties)
                    {
                        string key = property.Name;
                        string value;

                        value = CreateRegex(property.PropertyType, options, firstLevel: false);
                        builder.Add($"{key}:({(firstLevel ? $"?<{key}>" : "?:")}{value})");
                    }
                    result = $@"\{{{string.Join(",", builder)}\}}";
                }
                else
                {
                    throw new JsonParseException($"Type {returnType} is not implemented for deserializing.");
                }
                return result;
            }


            /// <summary>
            /// Converts the value of a type specified by a generic type parameter into a JSON string.
            /// </summary>
            /// <typeparam name="TValue">The type of the value to serialize.</typeparam>
            /// <param name="value">The value to convert.</param>
            /// <returns>A JSON string representation of the value.</returns>
            /// <exception cref="JsonParseException">Some type is not implemented for serializing.</exception>
            public static string Serialize<TValue>(TValue value, JsonParserOptions options)
            {
                return Serialize(value, typeof(TValue), options);
            }

            /// <summary>
            /// Converts the value of a specified type into a JSON string.
            /// </summary>
            /// <param name="value">The value to convert.</param>
            /// <param name="inputType">The type of the value to convert.</param>
            /// <returns>The JSON string representation of the value.</returns>
            /// <exception cref="JsonParseException">Some type is not implemented for deserializing.</exception>
            public static string Serialize(object value, Type inputType, JsonParserOptions options)
            {
                string result;
                IEnumerable<dynamic> properties;

                if (inputType == typeof(DateTime))
                {
                    var dateTime = ((DateTime)value);
                    var dateTimeUtc = dateTime.ToUniversalTime();
                    var difference = dateTime - dateTimeUtc;
                    result = $"\"{dateTime:yyyy-MM-ddTHH:mm:ss.fffffff}+{difference.Hours:00}:{difference.Minutes:00}\"";
                }
                else if (inputType == typeof(string))
                {
                    result = $"\"{value.ToString().Replace("\"", "\\\"")}\"";
                }
                else if (IsNumericType(inputType))
                {
                    result = NumberToString(inputType, value);
                }
                else if (options.IsEnumerable(inputType))
                {
                    var genericType = options.GetGenericType(inputType);
                    var etor = ((IEnumerable)value).GetEnumerator();
                    var items = new List<string>();

                    while (etor.MoveNext())
                    {
                        var current = etor.Current;

                        items.Add(Serialize(current, genericType, options));
                    }
                    result = $@"[{string.Join(",", items)}]";
                }
                else if ((properties = options.GetProperties(inputType)).Any())
                {
                    var item = new List<string>();

                    foreach (var property in properties)
                    {
                        item.Add($"{property.Name}:{Serialize(property.GetValue(value), property.PropertyType, options)}");
                    }
                    result = $"{{{string.Join(",", item)}}}";
                }
                else
                {
                    throw new JsonParseException($"Type {inputType.Name} is not implemented for deserializing.");
                }
                return result;
            }

            private static bool IsNumericType(Type type)
            {
                return type == typeof(sbyte)
                    || type == typeof(byte)
                    || type == typeof(short)
                    || type == typeof(ushort)
                    || type == typeof(int)
                    || type == typeof(uint)
                    || type == typeof(long)
                    || type == typeof(ulong)
                    || type == typeof(float)
                    || type == typeof(double)
                    || type == typeof(decimal)
                ;
            }

            private static object ParseNumber(Type type, string value)
            {
                object result;

                if (type == typeof(sbyte))
                {
                    result = sbyte.Parse(value);
                }
                else if (type == typeof(byte))
                {
                    result = byte.Parse(value);
                }
                else if (type == typeof(short))
                {
                    result = short.Parse(value);
                }
                else if (type == typeof(ushort))
                {
                    result = ushort.Parse(value);
                }
                else if (type == typeof(int))
                {
                    result = int.Parse(value);
                }
                else if (type == typeof(uint))
                {
                    result = uint.Parse(value);
                }
                else if (type == typeof(long))
                {
                    result = long.Parse(value);
                }
                else if (type == typeof(ulong))
                {
                    result = ulong.Parse(value);
                }
                else if (type == typeof(float))
                {
                    result = float.Parse(value);
                }
                else if (type == typeof(double))
                {
                    result = double.Parse(value);
                }
                else if (type == typeof(decimal))
                {
                    result = decimal.Parse(value);
                }
                else
                {
                    throw new NotSupportedException();
                }
                return result;
            }

            private static string NumberToString(Type type, object value)
            {
                string result;

                if (type == typeof(sbyte))
                {
                    result = ((sbyte)value).ToString();
                }
                else if (type == typeof(byte))
                {
                    result = ((byte)value).ToString();
                }
                else if (type == typeof(short))
                {
                    result = ((short)value).ToString();
                }
                else if (type == typeof(ushort))
                {
                    result = ((ushort)value).ToString();
                }
                else if (type == typeof(int))
                {
                    result = ((int)value).ToString();
                }
                else if (type == typeof(uint))
                {
                    result = ((uint)value).ToString();
                }
                else if (type == typeof(long))
                {
                    result = ((long)value).ToString();
                }
                else if (type == typeof(ulong))
                {
                    result = ((ulong)value).ToString();
                }
                else if (type == typeof(float))
                {
                    result = ((float)value).ToString();
                }
                else if (type == typeof(double))
                {
                    result = ((double)value).ToString();
                }
                else if (type == typeof(decimal))
                {
                    result = ((decimal)value).ToString();
                }
                else
                {
                    throw new NotSupportedException();
                }
                return result;
            }
        }
    }
}
