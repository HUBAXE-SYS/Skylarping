/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// The shared window classes find a theme's elements by name, which the compiler can't check:
// a theme missing one would only fail when the window opened.
/*==========================================================*/

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Skylarping.Tests
{
    [TestFixture]
    public class ThemeWindowTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [TestCase(typeof(Skype4.Main), typeof(MainWindowBase))]
        [TestCase(typeof(Skype5.Main), typeof(MainWindowBase))]
        [TestCase(typeof(Skype6.Main), typeof(MainWindowBase))]
        [TestCase(typeof(Skype4.Main), typeof(SkinnedMainWindowBase))]
        [TestCase(typeof(Skype5.Main), typeof(SkinnedMainWindowBase))]
        [TestCase(typeof(Skype4.Login), typeof(LoginWindowBase))]
        [TestCase(typeof(Skype5.Login), typeof(LoginWindowBase))]
        [TestCase(typeof(Skype6.Login), typeof(LoginWindowBase))]
        public void ThemeHasEveryElementTheSharedCodeLooksUp(Type theme, Type shared)
        {
            var parts = shared
                .GetProperties(Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.GetGetMethod(true).IsPrivate
                    && (p.PropertyType.FullName.StartsWith("System.Windows.") || p.PropertyType == typeof(SliceControl)))
                .Where(p => p.Name != "StartupTabButton") // worked out in code, not an element
                .ToList();
            Assert.That(parts, Is.Not.Empty);

            foreach (PropertyInfo part in parts)
            {
                FieldInfo field = theme.GetField(part.Name, Instance | BindingFlags.DeclaredOnly);
                Assert.That(field, Is.Not.Null, part.Name + " is missing from " + theme.FullName);
                Assert.That(part.PropertyType.IsAssignableFrom(field.FieldType), Is.True,
                    part.Name + " is a " + field.FieldType + " in " + theme.FullName);
            }
        }
    }
}
