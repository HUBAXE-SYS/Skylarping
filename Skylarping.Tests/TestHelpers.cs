/*==========================================================*/
// Copyright © The Skymu Team and other contributors.
// For any inquiries or concerns, email contact@skymu.app.
/*==========================================================*/
// Modification or redistribution of this code is governed
// by the terms set out in the project license agreement.
// If you do not comply with those terms, you may not
// modify or distribute any original code from the project.
/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

// The tests were written for xUnit, which creates a new test-class instance for
// every test (constructor = setup, Dispose = teardown). Keep that behaviour in NUnit.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]

namespace Skylarping.Tests
{
    /// <summary>
    /// The few assertions NUnit's classic API doesn't have in the same shape:
    /// ones that return the checked value, and predicate-based Contains.
    /// </summary>
    internal static class Check
    {
        /// <summary>Asserts <paramref name="value"/> is exactly of type <typeparamref name="T"/> and returns it.</summary>
        public static T IsType<T>(object value)
        {
            Assert.IsNotNull(value, "Expected an instance of " + typeof(T).Name + " but got null.");
            Assert.AreEqual(typeof(T), value.GetType());
            return (T)value;
        }

        /// <summary>Asserts the sequence has exactly one element and returns it.</summary>
        public static T Single<T>(IEnumerable<T> items)
        {
            List<T> list = items.ToList();
            Assert.AreEqual(1, list.Count, "Expected exactly one item.");
            return list[0];
        }

        /// <summary>Asserts at least one element matches <paramref name="predicate"/>.</summary>
        public static void Contains<T>(IEnumerable<T> items, Func<T, bool> predicate)
        {
            Assert.IsTrue(items.Any(predicate), "No item matched the condition.");
        }

        /// <summary>Asserts the sequence contains <paramref name="expected"/>.</summary>
        public static void Contains<T>(T expected, IEnumerable<T> items)
        {
            Assert.IsTrue(items.Contains(expected), "Expected item not found: " + expected);
        }

        /// <summary>Asserts <paramref name="actual"/> contains the substring <paramref name="expected"/>.</summary>
        public static void Contains(string expected, string actual)
        {
            StringAssert.Contains(expected, actual);
        }

        /// <summary>
        /// Rethrows an exception caught on another thread, keeping its original stack trace
        /// (.NET 4.0 stand-in for ExceptionDispatchInfo.Capture(ex).Throw()).
        /// </summary>
        public static Exception Rethrow(Exception ex)
        {
            MethodInfo preserve = typeof(Exception).GetMethod(
                "InternalPreserveStackTrace", BindingFlags.Instance | BindingFlags.NonPublic);
            if (preserve != null)
                preserve.Invoke(ex, null);
            throw ex;
        }
    }

    /// <summary>xUnit's Record.Exception: runs the code and returns what it threw, or null.</summary>
    internal static class Record
    {
        public static Exception Exception(Action code)
        {
            try
            {
                code();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public static Exception Exception(Func<object> code)
        {
            try
            {
                code();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
