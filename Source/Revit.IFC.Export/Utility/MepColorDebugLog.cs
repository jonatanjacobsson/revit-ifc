//
// Debug instrumentation for MEP system type graphic override export (session 912650).
// Writes NDJSON to c:\revit-worker\debug-912650.log
//
using System;
using System.IO;
using System.Text;

namespace Revit.IFC.Export.Utility
{
   internal static class MepColorDebugLog
   {
      private const string LogPath = @"c:\revit-worker\debug-912650.log";
      private const string SessionId = "912650";

      public static void Write(string hypothesisId, string location, string message, string dataJson = "{}")
      {
         // #region agent log
         try
         {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line = string.Format(
               "{{\"sessionId\":\"{0}\",\"hypothesisId\":\"{1}\",\"location\":\"{2}\",\"message\":\"{3}\",\"data\":{4},\"timestamp\":{5}}}",
               SessionId,
               Escape(hypothesisId),
               Escape(location),
               Escape(message),
               string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson,
               ts);
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
         }
         catch
         {
         }
         // #endregion
      }

      private static string Escape(string value)
      {
         if (string.IsNullOrEmpty(value))
            return string.Empty;
         return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
      }
   }
}
