using System;
using Diz.Core.Interfaces;

namespace Diz.Core.Mesen2
{
    /// <summary>
    /// Factory implementation for creating Mesen2 streaming clients
    /// </summary>
    public class Mesen2StreamingClientFactory : IMesen2StreamingClientFactory
    {
        private readonly IMesen2Configuration _defaultConfiguration;

        public Mesen2StreamingClientFactory(IMesen2Configuration defaultConfiguration)
        {
            _defaultConfiguration = defaultConfiguration ?? throw new ArgumentNullException(nameof(defaultConfiguration));
        }

        /// <summary>
        /// Create a new streaming client instance with default configuration
        /// </summary>
        public IMesen2StreamingClient CreateClient()
        {
            return new Mesen2StreamingClient(_defaultConfiguration);
        }

        /// <summary>
        /// Create a streaming client with specific configuration
        /// </summary>
        public IMesen2StreamingClient CreateClient(IMesen2Configuration configuration)
        {
            return new Mesen2StreamingClient(configuration ?? _defaultConfiguration);
        }
    }
}