//-----------------------------------------------------------------------
// <copyright company="CoApp Project">
//     Copyright (c) 2010-2013 Garrett Serack and CoApp Contributors. 
//     Contributors can be discovered using the 'git log' command.
//     All rights reserved.
// </copyright>
// <license>
//     The software is licensed under the Apache 2.0 License (the "License")
//     You may not use the software except in compliance with the License. 
// </license>
//-----------------------------------------------------------------------

namespace ClrPlus.Scripting.MsBuild.Utility {
    using System;
    using System.Collections.Generic;
    using Core.Collections;

    public class ListWithOnChanged<T> : ObservableList<string> {
        public ListWithOnChanged (Action<ListWithOnChanged<T>> onChanged,  Func<IEnumerable<string>> initialItems = null ) {
            if (initialItems != null) {
                foreach (var i in initialItems()) {
                    Add(i);
                }
            }
            ListChanged += (source, args) => onChanged(this);
        }
    }
}