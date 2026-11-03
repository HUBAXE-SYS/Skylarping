/*==========================================================*/
// License: https://skymu.app/legal/license
// SPDX-License-Identifier: AGPL-3.0-or-later
/*==========================================================*/
// Minimal replacement for CommunityToolkit.Mvvm's ObservableObject (which has no .NET 4.0
// build). Same SetProperty semantics: only raises PropertyChanged when the value changes.
/*==========================================================*/

using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Skylarping.Mvvm
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>Sets the field and raises PropertyChanged if the value changed. Returns true if it changed.</summary>
        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, newValue))
                return false;

            field = newValue;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
