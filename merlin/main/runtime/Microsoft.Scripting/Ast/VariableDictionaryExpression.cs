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

using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.Scripting.Runtime;
using Microsoft.Scripting.Utils;

namespace Microsoft.Scripting.Ast {

    public partial class Utils {
        public static Expression VariableDictionary(params ParameterExpression[] variables) {
            return VariableDictionary((IEnumerable<ParameterExpression>)variables);
        }
        public static Expression VariableDictionary(IEnumerable<ParameterExpression> variables) {
            var vars = variables.ToReadOnly();
            return Expression.New(
                typeof(LocalsDictionary).GetConstructor(new[] { typeof(IList<IStrongBox>), typeof(SymbolId[]) }),
                Expression.RuntimeVariables(vars),
                Expression.Constant(vars.Map(v => SymbolTable.StringToIdOrEmpty(v.Name)))
            );
        }
    }
}
