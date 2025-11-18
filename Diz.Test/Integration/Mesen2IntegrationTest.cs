using System;
using System.Threading.Tasks;
using Diz.Controllers.interfaces;
using Diz.Controllers.services;
using Diz.Core.Interfaces;
using Diz.Core.Mesen2;
using LightInject;

namespace Diz.Test.Integration
{
    /// <summary>
    /// Simple integration test to demonstrate that the Mesen2 integration components
    /// are properly configured and can be instantiated.
    /// </summary>
    public class Mesen2IntegrationTest
    {
        public static async Task RunBasicIntegrationTest()
        {
            // Create a LightInject service container
            var container = new ServiceContainer();
            
            // Register core Mesen2 services
            RegisterMesen2Services(container);
            
            try
            {
                Console.WriteLine("=== Mesen2 Integration Test ===");
                Console.WriteLine();
                
                // Test 1: Verify services can be resolved
                Console.WriteLine("Test 1: Service Resolution");
                TestServiceResolution(container);
                Console.WriteLine("✓ All services resolved successfully");
                Console.WriteLine();
                
                // Test 2: Test configuration
                Console.WriteLine("Test 2: Configuration Test");
                TestConfiguration(container);
                Console.WriteLine("✓ Configuration working correctly");
                Console.WriteLine();
                
                // Test 3: Test integration controller
                Console.WriteLine("Test 3: Integration Controller Test");
                await TestIntegrationController(container);
                Console.WriteLine("✓ Integration controller working correctly");
                Console.WriteLine();
                
                Console.WriteLine("=== All Tests Passed ===");
                Console.WriteLine("The Mesen2 integration is properly configured and ready to use!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private static void RegisterMesen2Services(ServiceContainer container)
        {
            // Register Mesen2 streaming services
            container.Register<IMesen2StreamingClient, Mesen2StreamingClient>(new PerContainerLifetime());
            container.Register<IMesen2StreamingClientFactory, Mesen2StreamingClientFactory>();
            
            // Register Mesen2 configuration service
            container.Register<IMesen2Configuration, Mesen2Configuration>();
            
            // Mock the GUI service for testing
            container.Register<ICommonGui>(factory => new MockCommonGui());
            
            // Register the integration controller
            container.Register<IMesen2IntegrationController, Mesen2IntegrationController>(new PerContainerLifetime());
        }
        
        private static void TestServiceResolution(ServiceContainer container)
        {
            // Try to resolve all key services
            var config = container.GetInstance<IMesen2Configuration>();
            var factory = container.GetInstance<IMesen2StreamingClientFactory>();
            var client = container.GetInstance<IMesen2StreamingClient>();
            var controller = container.GetInstance<IMesen2IntegrationController>();
            var gui = container.GetInstance<ICommonGui>();
            
            if (config == null) throw new Exception("Failed to resolve IMesen2Configuration");
            if (factory == null) throw new Exception("Failed to resolve IMesen2StreamingClientFactory");
            if (client == null) throw new Exception("Failed to resolve IMesen2StreamingClient");
            if (controller == null) throw new Exception("Failed to resolve IMesen2IntegrationController");
            if (gui == null) throw new Exception("Failed to resolve ICommonGui");
        }
        
        private static void TestConfiguration(ServiceContainer container)
        {
            var config = container.GetInstance<IMesen2Configuration>();
            
            // Test default values
            if (config.DefaultHost != "localhost") throw new Exception("Default DefaultHost incorrect");
            if (config.DefaultPort != 1234) throw new Exception("Default DefaultPort incorrect");
            if (config.ConnectionTimeoutMs != 5000) throw new Exception("Default ConnectionTimeoutMs incorrect");
            
            // Test configuration changes
            config.DefaultHost = "192.168.1.100";
            config.DefaultPort = 8080;
            config.ConnectionTimeoutMs = 10000;
            config.AutoReconnect = true;
            config.VerboseLogging = true;
            
            if (config.DefaultHost != "192.168.1.100") throw new Exception("DefaultHost update failed");
            if (config.DefaultPort != 8080) throw new Exception("DefaultPort update failed");
            if (config.ConnectionTimeoutMs != 10000) throw new Exception("ConnectionTimeoutMs update failed");
            if (!config.AutoReconnect) throw new Exception("AutoReconnect update failed");
            if (!config.VerboseLogging) throw new Exception("VerboseLogging update failed");
        }
        
        private static async Task TestIntegrationController(ServiceContainer container)
        {
            var controller = container.GetInstance<IMesen2IntegrationController>();
            
            // Verify controller properties
            if (controller.Configuration == null) throw new Exception("Controller.Configuration is null");
            
            // Test initialization
            controller.Initialize();
            
            // Test connection configuration
            controller.ShowConnectionDialog(); // Should not throw
            controller.ShowAdvancedConfigurationDialog(); // Should not throw
            
            // Test UI methods
            controller.ShowStatusWindow(); // Should not throw
            controller.ShowTraceViewer(); // Should not throw  
            controller.ShowDashboard(); // Should not throw
            
            // Test connection (will fail since no real server, but shouldn't throw)
            var connected = await controller.ConnectToMesen2Async();
            // Expected to fail in test environment
            
            // Test cleanup
            controller.Shutdown();
        }
    }
    
    /// <summary>
    /// Mock implementation of ICommonGui for testing
    /// </summary>
    public class MockCommonGui : ICommonGui
    {
        public bool PromptToConfirmAction(string msg)
        {
            Console.WriteLine($"[MockGUI] Confirmation prompt: {msg}");
            return true;
        }
        
        public void ShowError(string msg)
        {
            Console.WriteLine($"[MockGUI] Error: {msg}");
        }
        
        public void ShowWarning(string msg)
        {
            Console.WriteLine($"[MockGUI] Warning: {msg}");
        }
        
        public void ShowMessage(string msg)
        {
            Console.WriteLine($"[MockGUI] Message: {msg}");
        }
    }
}