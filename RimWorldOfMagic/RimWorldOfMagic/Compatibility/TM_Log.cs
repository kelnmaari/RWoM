using System;
using System.Collections;
using System.Reflection;
using System.Threading;

namespace TorannMagic.Compatibility
{
    /// <summary>
    /// Optional RimLogging integration. Never loads, initializes or ships the framework.
    /// Use warnings/errors for failures, not successful casts; Trace is developer-only.
    /// </summary>
    public static class TM_Log
    {
        // API verified against RimWorks/rimworld-logging-framework, Log.cs/Logging.cs,
        // revision 39b3b6507cdd2fda7e47ddb0916d3d9f7d4f8f76 (MIT).
        private const string AssemblyName = "RimWorks.RimLogging";
        private static readonly object discoveryLock = new object();
        private static int generation;
        private static int discoveredGeneration = -1;
        private static Provider provider;
        private static Func<Type[]> findTypes = FindTypes;
        private static Action<string, string> fallback = WriteVerse;
        [ThreadStatic] private static int depth;
        [ThreadStatic] private static bool insideFallback;

        static TM_Log()
        {
            // A negative discovery result is cached too. Late-loaded/reloaded assemblies
            // invalidate it without polling or reflecting on every gameplay tick.
            try { AppDomain.CurrentDomain.AssemblyLoad += (_, __) => Invalidate(); }
            catch { /* Logging must remain usable even if runtime event access fails. */ }
        }

        public static void Message(string message) { Message(null, message); }
        public static void Message(string category, string message) { Write("Message", category, message, null); }
        public static void Warning(string message) { Warning(null, message); }
        public static void Warning(string category, string message) { Write("Warning", category, message, null); }
        public static void Error(string message) { Error(null, message, null); }
        public static void Error(string message, Exception exception) { Error(null, message, exception); }
        public static void Error(string category, string message) { Error(category, message, null); }
        public static void Error(string category, string message, Exception exception) { Write("Error", category, message, exception); }

        /// <summary>Opt-in call sites only; never emits during normal production play.</summary>
        public static void Trace(string category, string message)
        {
            try
            {
                if (Verse.Prefs.DevMode) Write("Trace", category, message, null);
            }
            catch { /* Even developer diagnostics must not affect gameplay. */ }
        }

        private static void Write(string level, string category, string message, Exception exception)
        {
            try
            {
                string channel = string.IsNullOrWhiteSpace(category) ? "RWoM" : "RWoM." + category.Trim();
                message = message ?? string.Empty;
                // A sink may itself call a mod helper. Never send that event back to
                // the same sink, and never recursively call Verse from its fallback.
                if (depth != 0)
                {
                    if (level != "Trace") Fallback(level, channel, message, exception);
                    return;
                }
                depth++;
                try
                {
                    Provider current = GetProvider();
                    if (current != null)
                    {
                        try
                        {
                            if (current.TryWrite(level, channel, message, exception)) return;
                        }
                        catch { /* Preserve the original event, not the provider failure. */ }
                    }
                    // Trace has no normal-log fallback: an absent/older framework must
                    // not turn optional high-frequency diagnostics into message spam.
                    if (level != "Trace") Fallback(level, channel, message, exception);
                }
                finally { depth--; }
            }
            catch { /* No exception from logging is allowed across the caller boundary. */ }
        }

        private static void Fallback(string level, string channel, string message, Exception exception)
        {
            if (insideFallback) return;
            insideFallback = true;
            try
            {
                string text = "[" + channel + "] " + message;
                if (exception != null)
                {
                    try { text += "\n" + exception; }
                    catch { text += "\nException details unavailable (exception formatter failed)."; }
                }
                fallback(level, text);
            }
            catch { /* A broken game logger cannot be repaired by recursively logging. */ }
            finally { insideFallback = false; }
        }

        private static void WriteVerse(string level, string text)
        {
            if (level == "Error") Verse.Log.Error(text);
            else if (level == "Warning") Verse.Log.Warning(text);
            else Verse.Log.Message(text);
        }

        private static void Invalidate() { Interlocked.Increment(ref generation); }

        private static Provider GetProvider()
        {
            int observed = Volatile.Read(ref generation);
            if (Volatile.Read(ref discoveredGeneration) == observed) return provider;
            lock (discoveryLock)
            {
                observed = Volatile.Read(ref generation);
                if (discoveredGeneration == observed) return provider;
                Provider discovered = null;
                try
                {
                    Type[] types = findTypes();
                    if (types != null && types.Length == 2) discovered = Provider.Bind(types[0], types[1]);
                }
                catch { /* Missing dependencies and incompatible versions are optional. */ }
                provider = discovered;
                Volatile.Write(ref discoveredGeneration, observed);
                return discovered;
            }
        }

        private static Type[] FindTypes()
        {
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = loaded.Length - 1; i >= 0; i--)
            {
                try
                {
                    if (loaded[i].GetName().Name != AssemblyName) continue;
                    Type log = loaded[i].GetType("RimWorks.RimLogging.Log", false);
                    Type lifecycle = loaded[i].GetType("RimWorks.RimLogging.Logging", false);
                    if (log != null && lifecycle != null) return new[] { log, lifecycle };
                }
                catch { /* Continue past an unavailable assembly; never Assembly.Load it. */ }
            }
            return null;
        }

        private sealed class Provider
        {
            private readonly Action<string, string, object, int, string> info, warn, error, trace;
            private readonly Action<string, Exception, string, int, string> exceptionError;
            private readonly Func<bool> isPrimary;
            private readonly Func<IEnumerable> registeredSinks;

            private Provider(Type log, Type lifecycle)
            {
                info = BindMessage(log, "InfoTo");
                warn = BindMessage(log, "WarnTo");
                error = BindMessage(log, "ErrorTo");
                // Trace is optional, so an older facade can still preserve failures.
                try { trace = BindMessage(log, "TraceTo"); } catch { }
                exceptionError = Bind<Action<string, Exception, string, int, string>>(log, "ErrorTo",
                    typeof(string), typeof(Exception), typeof(string), typeof(int), typeof(string));
                isPrimary = Bind<Func<bool>>(lifecycle, "get_IsPrimary");
                registeredSinks = Bind<Func<IEnumerable>>(lifecycle, "get_RegisteredSinks");
            }

            internal static Provider Bind(Type log, Type lifecycle)
            {
                if (log == null || lifecycle == null) return null;
                return new Provider(log, lifecycle);
            }

            internal bool TryWrite(string level, string channel, string message, Exception exception)
            {
                // Public lifecycle API only. A facade can exist before bootstrap or in
                // degraded mode; then Verse remains the reliable owner of diagnostics.
                // Recheck readiness each call so early calls do not poison the cache.
                if (!isPrimary()) return false;
                IEnumerable sinks = registeredSinks();
                if (sinks == null) return false;
                var collection = sinks as ICollection;
                if (collection != null)
                {
                    if (collection.Count == 0) return false;
                }
                else
                {
                    IEnumerator iterator = sinks.GetEnumerator();
                    try { if (!iterator.MoveNext()) return false; }
                    finally { (iterator as IDisposable)?.Dispose(); }
                }
                if (level == "Error" && exception != null) exceptionError(channel, exception, message, 0, string.Empty);
                else
                {
                    var emit = level == "Error" ? error : level == "Warning" ? warn : level == "Trace" ? trace : info;
                    if (emit == null) return false;
                    // The object-context overload treats messages literally, so braces
                    // in exception text cannot become message-template arguments.
                    emit(channel, message, null, 0, string.Empty);
                }
                return true;
            }

            private static Action<string, string, object, int, string> BindMessage(Type type, string name)
            {
                return Bind<Action<string, string, object, int, string>>(type, name,
                    typeof(string), typeof(string), typeof(object), typeof(int), typeof(string));
            }

            private static T Bind<T>(Type type, string name, params Type[] parameters) where T : class
            {
                MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
                if (method == null) throw new MissingMethodException(type.FullName, name);
                return (T)(object)Delegate.CreateDelegate(typeof(T), method);
            }
        }
    }
}
