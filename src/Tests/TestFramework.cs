using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Aly.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertException : Exception
    {
        public AssertException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message = null)
        {
            if (!condition) throw new AssertException(message ?? "expected true");
        }

        public static void False(bool condition, string message = null)
        {
            if (condition) throw new AssertException(message ?? "expected false");
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertException(string.Format("{0}expected <{1}> but was <{2}>",
                    message == null ? "" : message + ": ", expected, actual));
        }

        public static void Near(double expected, double actual, double tolerance, string message = null)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new AssertException(string.Format("{0}expected {1} ± {2} but was {3}",
                    message == null ? "" : message + ": ", expected, tolerance, actual));
        }

        public static void Throws<TException>(Action action) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new AssertException("expected " + typeof(TException).Name);
        }
    }

    public static class Runner
    {
        public static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            var tests = Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                .Where(m => m.GetCustomAttributes(typeof(TestAttribute), false).Length > 0)
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();

            int failed = 0;
            var sw = Stopwatch.StartNew();
            foreach (MethodInfo m in tests)
            {
                string name = m.DeclaringType.Name + "." + m.Name;
                try
                {
                    object target = m.IsStatic ? null : Activator.CreateInstance(m.DeclaringType);
                    m.Invoke(target, null);
                }
                catch (TargetInvocationException tie)
                {
                    failed++;
                    Exception e = tie.InnerException ?? tie;
                    Console.WriteLine("FAIL  " + name);
                    Console.WriteLine("      " + (e is AssertException ? e.Message : e.ToString()));
                }
            }
            Console.WriteLine();
            Console.WriteLine("{0} tests, {1} failed ({2} ms)", tests.Count, failed, sw.ElapsedMilliseconds);
            return failed == 0 ? 0 : 1;
        }
    }
}
