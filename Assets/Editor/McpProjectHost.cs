#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ShootDemo.EditorTools
{
    /// <summary>
    /// Starts MCP for Unity with the project HTTP endpoint.
    /// Uses the package's public service API and stores no machine-specific paths.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpProjectHost
    {
        const string LocatorTypeName = "MCPForUnity.Editor.Services.MCPServiceLocator, MCPForUnity.Editor";
        const double GiveUpAfterSeconds = 180;
        const double AutoStartGraceSeconds = 8;

        static bool _launchRequested;
        static bool _bridgeRequested;
        static double _startedAt;
        static double _idleSince = -1;

        static McpProjectHost()
        {
            EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
            EditorPrefs.SetBool("MCPForUnity.HttpServerLaunchConfirmed", true);
            EditorPrefs.SetString("MCPForUnity.HttpTransportScope", "local");
            _startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup - _startedAt > GiveUpAfterSeconds)
            {
                Debug.LogWarning("[McpProjectHost] MCP for Unity did not become ready. Open Window > MCP for Unity and start the local HTTP server.");
                EditorApplication.update -= Tick;
                return;
            }

            Type locator = Type.GetType(LocatorTypeName);
            if (locator == null)
            {
                return;
            }

            object server = locator.GetProperty("Server", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object bridge = locator.GetProperty("Bridge", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (server == null || bridge == null)
            {
                return;
            }

            bool reachable = InvokeBool(server, "IsLocalHttpServerReachable");
            if (!reachable && !_launchRequested)
            {
                if (_idleSince < 0)
                {
                    _idleSince = EditorApplication.timeSinceStartup;
                }

                // Package auto-start owns the first launch. Starting here at the same
                // time stops the process it just spawned.
                if (EditorApplication.timeSinceStartup - _idleSince < AutoStartGraceSeconds)
                {
                    return;
                }

                _launchRequested = true;
                bool started = InvokeBool(server, "StartLocalHttpServer", true);
                Debug.Log("[McpProjectHost] StartLocalHttpServer(quiet) => " + started);
                return;
            }

            if (!reachable)
            {
                return;
            }

            bool bridgeRunning = GetBoolProperty(bridge, "IsRunning");
            if (bridgeRunning)
            {
                Debug.Log("[McpProjectHost] MCP HTTP bridge is running.");
                EditorApplication.update -= Tick;
                return;
            }

            if (_bridgeRequested)
            {
                return;
            }

            _bridgeRequested = true;
            MethodInfo startAsync = bridge.GetType().GetMethod("StartAsync", BindingFlags.Public | BindingFlags.Instance);
            startAsync?.Invoke(bridge, null);
            Debug.Log("[McpProjectHost] Requested MCP bridge StartAsync.");
        }

        static bool InvokeBool(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                return false;
            }

            object result = method.Invoke(target, args.Length == 0 ? null : args);
            return result is bool value && value;
        }

        static bool GetBoolProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(target) is bool value && value;
        }
    }
}
#endif
