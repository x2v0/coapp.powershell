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
    using Core.Collections;

    public class ListWithAction<T> : ObservableList<T> {
        public ListWithAction(Action<T> onAdded) {
            ItemAdded += (source, args) => onAdded(args.item);
        }

        public override int Add(object value) {
            if(!Contains(value)) {
                return base.Add(value);
            }
            return IndexOf(value);
        }

        public override void Add(T item) {
            if(!Contains(item)) {
                base.Add(item);
            }
        }

    }
}