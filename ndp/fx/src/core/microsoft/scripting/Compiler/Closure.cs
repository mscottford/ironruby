﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

namespace System.Runtime.CompilerServices {

    /// <summary>
    /// If the delegate generated by the Lambda compiler needs to either be a
    /// closure, or close over constants, the delegate itself will close over
    /// the instance of this object.
    /// 
    /// TODO: Prevent this from being exposed as public surface area. See what
    /// Linq v1 does with System.Runtime.CompilerServices.ExecutionScope
    /// </summary>
    public sealed class Closure {
        /// <summary>
        /// The constant pool
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1051:DoNotDeclareVisibleInstanceFields")]
        public readonly object[] Constants;

        /// <summary>
        /// The environment, which stores closed over variables from the parent
        /// scope
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2105:ArrayFieldsShouldNotBeReadOnly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1051:DoNotDeclareVisibleInstanceFields")]
        public readonly object[] Locals;
        
        public Closure(object[] constants, object[] locals) {
            Constants = constants;
            Locals = locals;
        }
    }
}
