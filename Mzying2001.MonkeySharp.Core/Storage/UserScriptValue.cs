using System;
using System.Globalization;
using System.Text.Json;

namespace Mzying2001.MonkeySharp.Core.Storage
{
    /// <summary>An immutable, typed userscript value, including JavaScript undefined and non-finite numbers.</summary>
    public sealed class UserScriptValue : IEquatable<UserScriptValue>
    {
        private UserScriptValue(string encodedValue) { EncodedValue = encodedValue; }

        /// <summary>Gets the typed storage encoding: u, b, n, s, or o followed by its payload.</summary>
        public string EncodedValue { get; }

        /// <summary>Gets a stored JavaScript undefined value. This is distinct from a missing key.</summary>
        public static UserScriptValue Undefined { get; } = new UserScriptValue("u");

        /// <summary>Validates and canonicalizes a typed storage encoding.</summary>
        /// <param name="encodedValue">The typed storage encoding.</param>
        /// <returns>An immutable userscript value.</returns>
        public static UserScriptValue FromEncoded(string encodedValue)
        {
            if (encodedValue == null) throw new ArgumentNullException(nameof(encodedValue));
            if (encodedValue == "u") return Undefined;
            if (encodedValue.Length == 0) throw new FormatException("A storage value requires a type prefix.");
            var payload = encodedValue.Substring(1);
            switch (encodedValue[0])
            {
                case 's': return new UserScriptValue(encodedValue);
                case 'b':
                    if (payload == "true" || payload == "false") return new UserScriptValue(encodedValue);
                    break;
                case 'n':
                    if (payload == "NaN" || payload == "Infinity" || payload == "-Infinity")
                        return new UserScriptValue(encodedValue);
                    using (var document = JsonDocument.Parse(payload))
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Number) break;
                        var number = document.RootElement.GetDouble();
                        if (double.IsNaN(number) || double.IsInfinity(number)) break;
                        return new UserScriptValue("n" + (number == 0 ? "0" : number.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant()));
                    }
                case 'o':
                    using (var document = JsonDocument.Parse(payload))
                    {
                        var kind = document.RootElement.ValueKind;
                        if (kind == JsonValueKind.Null || kind == JsonValueKind.Object || kind == JsonValueKind.Array)
                            return new UserScriptValue("o" + JsonSerializer.Serialize(document.RootElement));
                    }
                    break;
            }
            throw new FormatException("Invalid userscript storage value encoding.");
        }

        /// <summary>Creates a userscript value from JSON. JavaScript-only values use FromEncoded or Undefined.</summary>
        /// <param name="json">A JSON value.</param>
        /// <returns>An immutable userscript value.</returns>
        public static UserScriptValue FromJson(string json)
        {
            using (var document = JsonDocument.Parse(json))
            {
                var value = document.RootElement;
                switch (value.ValueKind)
                {
                    case JsonValueKind.String: return FromEncoded("s" + value.GetString());
                    case JsonValueKind.True: return FromEncoded("btrue");
                    case JsonValueKind.False: return FromEncoded("bfalse");
                    case JsonValueKind.Number: return FromEncoded("n" + value.GetRawText());
                    default: return FromEncoded("o" + value.GetRawText());
                }
            }
        }

        /// <inheritdoc />
        public bool Equals(UserScriptValue other) => other != null && EncodedValue == other.EncodedValue;
        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as UserScriptValue);
        /// <inheritdoc />
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(EncodedValue);
    }
}
