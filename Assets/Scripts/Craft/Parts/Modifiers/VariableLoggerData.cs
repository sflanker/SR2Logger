using System;
using ModApi.Craft.Parts;
using ModApi.Craft.Parts.Attributes;
using UnityEngine;

namespace Assets.Scripts.Craft.Parts.Modifiers
{

    [Serializable]
    [DesignerPartModifier("Logger")]
    [PartModifierTypeId("SR2Logger.VariableLogger")]
    public class VariableLoggerData : PartModifierData<VariableLoggerScript>
    {
        public const String DefaultHost = "localhost";
        public const Int32 DefaultPort = 2837;
        public const Int32 DefaultFrequency = 1000;

        [SerializeField]
        [DesignerPropertyTextInput(Label = "Hostname", Order = 99, Tooltip = "[Optional] Host to send UDP log data to.")]
        private String _host = DefaultHost;

        [SerializeField]
        [DesignerPropertyTextInput(Label = "Port", Tooltip = "The UDP port to send log data to.")]
        private String _port = DefaultPort.ToString();

        [SerializeField]
        [DesignerPropertyTextInput(Label = "Sampling Frequency", Tooltip = "The frequency in milliseconds with which to log sampled values. Note: when using time warp the frequency will be scaled by the time warp multiplier.")]
        private String _frequency = DefaultFrequency.ToString();

        [SerializeField]
        [DesignerPropertyTextInput(Label = "Path", Tooltip = "[Optional] The path of a file to append log messages to.")]
        private String _path = String.Empty;

        [SerializeField]
        [DesignerPropertyToggleButton(Label = "Include Log", Tooltip = "Include log messages in the output.")]
        private Boolean _includeLog = false;

        /// <summary>
        /// Gets the hostname.
        /// </summary>
        /// <value>
        /// The hostname.
        /// </value>
        public String Hostname => _host;

        /// <summary>
        /// Gets or sets the port number.
        /// </summary>
        /// <value>
        /// The port.
        /// </value>
        public Int32 Port
        {
            get
            {
                if (Int32.TryParse(_port, out var res))
                {
                    return res;
                }

                Debug.LogError($"Invalid port number: \"{_port}\". Using default port {DefaultPort} instead.");
                return DefaultPort;
            }
        }

        public Int32 Frequency
        {
            get
            {
                if (Int32.TryParse(_frequency, out var res) && res >= 0)
                {
                    return res;
                }

                Debug.LogError($"Invalid sampling frequency: \"{_frequency}\". Using default frequency {DefaultFrequency} instead.");
                return DefaultFrequency;
            }
        }

        public String Path => _path;

        public Boolean IncludeLog => _includeLog;
    }
}
