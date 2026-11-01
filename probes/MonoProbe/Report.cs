using System;
using System.Reflection;

namespace MonoProbe
{
    /// <summary>
    /// One line per measurement, `probe.measure = value`, so that the same probe run on two
    /// runtimes can be compared line by line. A measurement that throws is written as
    /// `! Type: message` in place of its value, and the probe carries on: a failure is an answer.
    /// </summary>
    internal static class Report
    {
        internal static void Line(string key, object value)
        {
            Console.WriteLine(key + " = " + Format(value));
        }

        internal static T Measure<T>(string key, Func<T> measure, Func<T, object> describe = null)
        {
            try
            {
                var value = measure();
                Line(key, describe == null || value == null ? (object)value : describe(value));
                return value;
            }
            catch (Exception error)
            {
                Line(key, Failure(error));
                return default(T);
            }
        }

        internal static void Step(string key, Action step)
        {
            try
            {
                step();
                Line(key, "ok");
            }
            catch (Exception error)
            {
                Line(key, Failure(error));
            }
        }

        internal static string Failure(Exception error)
        {
            while (error is TargetInvocationException && error.InnerException != null)
            {
                error = error.InnerException;
            }

            return "! " + error.GetType().FullName + ": " + OneLine(error.Message);
        }

        internal static string Hex(byte[] bytes, int count)
        {
            if (bytes == null)
            {
                return "(null)";
            }

            var length = Math.Min(count, bytes.Length);
            return BitConverter.ToString(bytes, 0, length).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Format(object value)
        {
            if (value == null)
            {
                return "(null)";
            }

            if (value is bool)
            {
                return (bool)value ? "true" : "false";
            }

            if (value is DateTime)
            {
                var time = (DateTime)value;
                return time.ToString("yyyy-MM-ddTHH:mm:ss.fffffff") + " kind " + time.Kind;
            }

            return OneLine(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
