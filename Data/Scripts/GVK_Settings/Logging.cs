using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI;

namespace AlwaysSpawnWithoutHydrogen {

    internal class Logger {

        private static Dictionary<String, Logger> INSTANCE_MAP = new Dictionary<string, Logger>();

        private readonly StringBuilder cache = new StringBuilder();
        private readonly TextWriter textWriter;

        private Logger(string logFile) {

            try {

                textWriter = MyAPIGateway.Utilities.WriteFileInLocalStorage(logFile, typeof(Logger));

            } catch {
            }
        }

        public static Logger getLogger(String fileName) {

            if (MyAPIGateway.Utilities == null)
                return null;

            Logger logger = null;

            if (INSTANCE_MAP.TryGetValue(fileName, out logger))
                return logger;

            logger = new Logger(fileName + ".log");

            INSTANCE_MAP.Add(fileName, logger);

            return logger;
        }

        public void WriteLine(string text) {

            try {

                if (cache.Length > 0)
                    textWriter.WriteLine(cache);

                cache.Clear();
                cache.Append(DateTime.Now.ToString("[HH:mm:ss] "));

                textWriter.WriteLine(cache.Append(text));
                textWriter.Flush();

                cache.Clear();

            } catch {
            }
        }

        public void Write(string text) {
            cache.Append(text);
        }

        internal void Close() {

            try {

                if (cache.Length > 0)
                    textWriter.WriteLine(cache);

                textWriter.Flush();
                textWriter.Close();

            } catch {
            }
        }
    }
}