using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ModApi.Craft.Parts;
using ModApi.Craft.Program;
using ModApi.GameLoop;
using ModApi.GameLoop.Interfaces;
using UnityEngine;

namespace Assets.Scripts.Craft.Parts.Modifiers
{
    public class VariableLoggerScript : PartModifierScript<VariableLoggerData>, IFlightUpdate
    {
        private const String Version = "0.1-beta";

        private static readonly FieldInfo _processField;
        private static readonly PropertyInfo _logServiceProperty;

        static VariableLoggerScript()
        {
            _processField = typeof(FlightProgramScript).GetField("_process", BindingFlags.NonPublic | BindingFlags.Instance);
            _logServiceProperty = typeof(Process).GetProperty("LogService", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private Boolean initialized;

        private FlightProgramScript flightProgramScript;

        private UdpServer server;
        private StreamWriter logFileWriter;

        private Single frequency;
        private Single timeSinceLastSample;
        private TimeSpan worldTime = TimeSpan.Zero;

        /// <summary>
        /// Initialises this instance.
        /// </summary>
        private Boolean EnsureInitialized()
        {
            if (!this.initialized)
            {
                Debug.Log($"Initializing {nameof(VariableLoggerScript)} {Version}");

                // get flight program
                this.flightProgramScript = PartScript.GetModifier<FlightProgramScript>();
                if (this.flightProgramScript == null)
                {
                    Debug.LogError("Logger script has no flight program: deactivating");
                    this.enabled = false;
                } else
                {
                    var process = (Process)_processField.GetValue(flightProgramScript);

                    // Inject tracing log service.
                    var tracer = new TracingLogService(this, process.LogService);
                    // Set the default ILogService for new threads
                    _logServiceProperty.SetValue(process, tracer);
                    // Update all existing threads
                    foreach (var thread in process.Threads)
                    {
                        thread.Context.Log = tracer;
                    }

                    if (!String.IsNullOrEmpty(this.Data.Hostname))
                    {
                        Debug.Log($"Logging Data To udp://{this.Data.Hostname}:{this.Data.Port}");
                        this.server = new UdpServer(this.Data.Hostname, this.Data.Port);
                    }

                    if (!String.IsNullOrEmpty(this.Data.Path))
                    {
                        var absPath = Path.Combine(Application.persistentDataPath, this.Data.Path);
                        Debug.Log($"Logging Data To '{absPath}'");
                        this.logFileWriter = new StreamWriter(absPath, append: true);
                    }

                    // Convert from frequency in milliseconds to frequency in seconds
                    this.frequency = this.Data.Frequency / 1000f;

                    this.initialized = true;
                    Debug.Log($"{nameof(VariableLoggerScript)} successfully initialized.");
                }
            }

            return this.initialized;
        }

        public void FlightUpdate(in FlightFrameData frame)
        {
            if (frame.IsPaused || !PartScript.Data.Activated || !this.EnsureInitialized())
            {
                return;
            }

            this.worldTime = TimeSpan.FromSeconds(frame.FlightScene.FlightState.Time);
            this.timeSinceLastSample += (float)frame.DeltaTimeWorld;
            var factor = (float)(frame.TimeManager.CurrentMode.TimeMultiplier > 2.0 ? (frame.TimeManager.CurrentMode.TimeMultiplier / 2.0) : 1.0);
            while (this.timeSinceLastSample > this.frequency * factor)
            {
                this.timeSinceLastSample -= this.frequency * factor;
                SampleVariables();
            }
        }

        private void LogMessage(String message, Int32 activeThreadId)
        {
            if (this.Data.IncludeLog)
            {
                var formattedMessage =
                    this.worldTime.TotalDays >= 1 ?
                        $"[{this.worldTime:d' days 'hh\\:mm\\:ss\\.fff}] <{activeThreadId}>: {message}" :
                        $"[{this.worldTime:hh\\:mm\\:ss\\.fff}] <{activeThreadId}>: {message}";

                this.server?.SendLogMessage(formattedMessage);
                if (this.logFileWriter != null)
                {
                    lock (this.logFileWriter)
                    {
                        this.logFileWriter.WriteLine(formattedMessage);
                        this.logFileWriter.Flush();
                    }
                }
            }
        }

        private void SampleVariables()
        {
            var stringBuilder = this.logFileWriter != null ? new StringBuilder() : null;
            this.server?.BeginVariableSample(this.worldTime);
            try
            {

                var first = true;
                var process = (Process)_processField.GetValue(flightProgramScript);
                foreach (var v in process.GlobalVariables.Variables)
                {
                    if (v.Name.StartsWith("log_") && v.Name.Length > 4)
                    {
                        var friendlyName = v.Name.Substring(4, v.Name.Length - 4);
                        this.server?.SendVariable(friendlyName, v.Value);
                        if (!first)
                        {
                            stringBuilder?.Append(", ");
                        } else
                        {
                            first = false;
                        }

                        stringBuilder?.Append($"'{friendlyName}': {ConvertToJson(v.Value)}");
                    }
                }
            }
            finally
            {
                this.server?.FinishVariableSample();
            }

            if (this.logFileWriter != null)
            {
                lock (this.logFileWriter)
                {
                    this.logFileWriter.WriteLine($"{{ 'timestamp': '{this.worldTime}', 'variables': {{ {stringBuilder} }} }}");
                    this.logFileWriter.Flush();
                }
            }
        }

        private static String ConvertToJson(ExpressionResult value)
        {
            switch (value.ExpressionType)
            {
                case ExpressionType.Boolean:
                    return value.BoolValue ? "true" : "false";
                case ExpressionType.Number:
                    return value.NumberValue.ToString("R");
                case ExpressionType.Text:
                    return $"'{Quote(value.TextValue)}'";
                case ExpressionType.Vector:
                    return $"{{ 'x': {value.VectorValue.x}, 'y': {value.VectorValue.y}, 'z': {value.VectorValue.z} }}";
                case ExpressionType.List:
                    return $"[{String.Join(", ", value.ListValue.Select(Quote))}]";
                default:
                    Debug.LogWarning("Cannot log type: " + value.ExpressionType);
                    return "null";
            }
        }

        private static String Quote(String value)
        {
            return value.Replace("\\", "\\\\").Replace("'", "\\'");
        }

        public override void FlightEnd()
        {
            this.logFileWriter?.Dispose();
            this.server?.Dispose();
        }

        private class TracingLogService : ILogService
        {
            private readonly VariableLoggerScript parent;
            private readonly ILogService innerLogService;

            public TracingLogService(VariableLoggerScript parent, ILogService innerLogService)
            {
                this.parent = parent;
                this.innerLogService = innerLogService;
            }

            public void Log(string message, IThreadContext context = null, ProgramNode node = null)
            {
                this.parent.LogMessage(message, this.innerLogService.ActiveThreadId ?? -1);
                this.innerLogService.Log(message, context, node);
            }

            public void LogError(string message, IThreadContext context = null, ProgramNode node = null)
            {
                this.innerLogService.LogError(message, context, node);
            }

            public int? ActiveThreadId
            {
                get => this.innerLogService.ActiveThreadId;
                set => this.innerLogService.ActiveThreadId = value;
            }
        }
    }
}
