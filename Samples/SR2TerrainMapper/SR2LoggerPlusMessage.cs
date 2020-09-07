using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SR2TerrainMapper {
    public abstract class SR2LoggerPlusMessage {
        public abstract MessageType Type { get; }

        public static SR2LoggerPlusMessage Deserialize(Byte[] data) {
            switch ((MessageType)data[0]) {
                case MessageType.VariableSample: {
                    var span = data.AsSpan().Slice(1);
                    var timestamp = BitConverter.ToUInt64(span.Slice(0, sizeof(UInt64)));
                    span = span.Slice(sizeof(UInt64));

                    var variables = new Dictionary<String, SR2VariableValue>();
                    while (span.Length > 0) {
                        var name = ReadString(ref span);
                        SR2VariableValue variable;
                        switch ((VariableType)span[0]) {
                            case VariableType.Float64:
                                variable = new SR2VariableValue(BitConverter.ToDouble(span.Slice(1, sizeof(Double))));
                                span = span.Slice(1 + sizeof(Double));
                                break;
                            case VariableType.Boolean:
                                variable = new SR2VariableValue(BitConverter.ToBoolean(span.Slice(1, sizeof(Boolean))));
                                span = span.Slice(1 + sizeof(Boolean));
                                break;
                            case VariableType.Vector3d:
                                span = span.Slice(1);
                                variable = new SR2VariableValue(new Vector3d(
                                    BitConverter.ToDouble(span.Slice(0, sizeof(Double))),
                                    BitConverter.ToDouble(span.Slice(sizeof(Double), sizeof(Double))),
                                    BitConverter.ToDouble(span.Slice(sizeof(Double) * 2, sizeof(Double)))
                                ));
                                span = span.Slice(sizeof(Double) * 3);
                                break;
                            case VariableType.Text:
                                variable = new SR2VariableValue(ReadString(ref span));
                                break;
                            case VariableType.List:
                                var length = BitConverter.ToInt32(span.Slice(1, sizeof(Int32)));
                                span = span.Slice(1 + sizeof(Int32));
                                var list = new List<String>(length);
                                for (var i = 0; i < length; i++) {
                                    list.Add(ReadString(ref span));
                                }
                                variable = new SR2VariableValue(list);
                                break;
                            case VariableType.None:
                                span = span.Slice(1);
                                variable = null;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        variables.Add(name, variable);
                    }

                    return new SR2LoggerPlusVariableMessage(
                        TimeSpan.FromMilliseconds(timestamp),
                        variables
                    );
                }
                case MessageType.LogMessage: {
                    var span = data.AsSpan().Slice(1);
                    return new SR2LoggerPlusLogMessage(
                        ReadString(ref span)
                    );
                }
                case MessageType.None:
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static String ReadString(ref Span<Byte> span) {
            var length = BitConverter.ToInt32(span.Slice(0, sizeof(Int32)));
            if (length < 0) {
                span = span.Slice(sizeof(Int32));
                return null;
            } else {
                var str = Encoding.UTF8.GetString(span.Slice(sizeof(Int32), length));
                span = span.Slice(sizeof(Int32) + length);
                return str;
            }
        }
    }

    public class SR2LoggerPlusVariableMessage : SR2LoggerPlusMessage {
        public override MessageType Type => MessageType.VariableSample;

        public TimeSpan Timestamp { get; }
        public IReadOnlyDictionary<String, SR2VariableValue> Variables { get; }

        public SR2LoggerPlusVariableMessage(
            TimeSpan timestamp,
            IReadOnlyDictionary<String, SR2VariableValue> variables) {

            this.Timestamp = timestamp;
            this.Variables = variables;
        }
    }

    public class SR2VariableValue {
        private Boolean? @bool;
        private Double? number;
        private String text;
        private Vector3d? vector;
        private List<String> list;

        public VariableType Type { get; }

        /// <summary>
        /// Gets a value indicating whether this expression result is true. If this expression
        /// is not of this type, then it will attempt to calculate/cast it from its currently
        /// set type.
        /// </summary>
        /// <value>The value of this variable as a bool.</value>
        public bool BoolValue {
            get {
                this.@bool ??= this.CastToBool();
                return this.@bool.Value;
            }
        }

        /// <summary>
        /// Gets the value of this expression result as a number. If this expression
        /// is not of this type, then it will attempt to calculate/cast it from its currently
        /// set type.
        /// </summary>
        /// <value>The value of this variable as a number.</value>
        public double NumberValue {
            get {
                this.number ??= this.CastToNumber();
                return this.number.Value;
            }
        }

        /// <summary>
        /// Gets the value of this expression result as a string. If this expression
        /// is not of this type, then it will attempt to calculate/cast it from its currently
        /// set type.
        /// </summary>
        /// <value>The value of this variable as a string.</value>
        public string TextValue => this.text ??= this.CastToText();

        /// <summary>
        /// Gets the value of this expression result as a vector. If this expression
        /// is not of this type, then it will attempt to calculate/cast it from its currently
        /// set type.
        /// </summary>
        /// <value>The value of this variable as a vector.</value>
        public Vector3d VectorValue {
            get {
                this.vector ??= this.CastToVector();
                return this.vector.Value;
            }
        }

        /// <summary>Gets the list value.</summary>
        /// <value>The list value.</value>
        public IReadOnlyList<String> ListValue {
            get {
                this.list ??= this.CastToList();
                return this.list;
            }
        }

        public SR2VariableValue(Boolean @bool) {
            this.Type = VariableType.Boolean;
            this.@bool = @bool;
        }

        public SR2VariableValue(Double number) {
            this.Type = VariableType.Float64;
            this.number = number;
        }

        public SR2VariableValue(String text) {
            this.Type = VariableType.Text;
            this.text = text;
        }

        public SR2VariableValue(Vector3d vector) {
            this.Type = VariableType.Vector3d;
            this.vector = vector;
        }

        public SR2VariableValue(List<String> list) {
            this.Type = VariableType.List;
            this.list = list;
        }

        /// <summary>Casts the current expression to bool.</summary>
        /// <returns>The bool result.</returns>
        private bool CastToBool() {
            switch (this.Type) {
                case VariableType.Boolean:
                    return this.@bool.Value;
                case VariableType.Text:
                    return this.text.ToLower() == "true" || this.NumberValue != 0.0;
                case VariableType.Float64:
                    return this.number.Value != 0.0;
                case VariableType.Vector3d:
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>Casts the current expression to a list.</summary>
        /// <returns>The list result.</returns>
        private List<string> CastToList() {
            // Todo?
            return new List<string>();
        }

        /// <summary>Casts the current expression to a number.</summary>
        /// <returns>The number result.</returns>
        private double CastToNumber() {
            switch (this.Type) {
                case VariableType.Boolean:
                    return this.@bool.Value ? 1.0 : 0.0;
                case VariableType.Text:
                    Double.TryParse(this.text, out var result);
                    return result;
                case VariableType.Float64:
                    return this.number.Value;
                case VariableType.Vector3d:
                    return this.vector.Value.Magnitude;
                default:
                    return 0.0;
            }
        }

        /// <summary>Casts the current expression to text.</summary>
        /// <returns>The text result.</returns>
        private string CastToText() {
            switch (this.Type) {
                case VariableType.Boolean:
                    return this.@bool.Value.ToString();
                case VariableType.List:
                    return $"List with {this.list.Count} item(s)";
                case VariableType.Text:
                    return this.text;
                case VariableType.Float64:
                    return this.number.Value.ToString();
                case VariableType.Vector3d:
                    return this.vector.Value.ToString();
                default:
                    return string.Empty;
            }
        }

        /// <summary>Casts the current expression to a vector.</summary>
        /// <returns>The vector result.</returns>
        private Vector3d CastToVector() {
            switch (this.Type) {
                case VariableType.Boolean:
                    return Vector3d.Zero;
                case VariableType.Text:
                    Vector3d.TryParse(this.text, out var result);
                    return result;
                case VariableType.Float64:
                    return Vector3d.Zero;
                case VariableType.Vector3d:
                    return this.vector.Value;
                default:
                    return Vector3d.Zero;
            }
        }
    }

    public readonly struct Vector3d {
        public static readonly Vector3d Zero = new Vector3d();
        private readonly Double x;
        private readonly Double y;
        private readonly Double z;

        public Double X => this.x;
        public Double Y => this.y;
        public Double Z => this.z;

        public Double Magnitude => Math.Sqrt(Math.Pow(this.x, 2) + Math.Pow(this.y, 2) + Math.Pow(this.z, 2));

        public Vector3d(Double x, Double y, Double z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        private static readonly Regex VectorRegex = new Regex(@"^\(([^,)]*),([^,)]*),([^,)]*)\)$", RegexOptions.Compiled);

        public static Boolean TryParse(String str, out Vector3d result) {
            var match = VectorRegex.Match(str.Trim());
            if (match.Success &&
                Double.TryParse(match.Groups[0].Value.Trim(), out var _x) &&
                Double.TryParse(match.Groups[0].Value.Trim(), out var _y) &&
                Double.TryParse(match.Groups[0].Value.Trim(), out var _z)) {

                result = new Vector3d(_x, _y, _z);
                return true;
            } else {
                result = Zero;
                return false;
            }
        }

        public override string ToString() {
            return $"({x:R}, {y:R}, {z:R})";
        }
    }

    public class SR2LoggerPlusLogMessage : SR2LoggerPlusMessage {
        public override MessageType Type => MessageType.LogMessage;

        public String LogMessage { get; }

        public SR2LoggerPlusLogMessage(String logMessage) {
            this.LogMessage = logMessage;
        }
    }

    public enum MessageType : Byte {
        None = 0,
        VariableSample,
        LogMessage
    }

    public enum VariableType : Byte {
        None = 0,
        Float64,
        Boolean,
        Vector3d,
        Text,
        List
    }
}
