
/* ****************************************************************************
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
using System.Diagnostics;
using System.Reflection;
using System.Dynamic.Utils;
using System.Text;

namespace System.Linq.Expressions {
    //CONFORMING
    public class BinaryExpression : Expression {
        private readonly Expression _left;
        private readonly Expression _right;

        internal BinaryExpression(Expression left, Expression right) {
            _left = left;
            _right = right;
        }

        public override bool CanReduce {
            get {
                //Only OpAssignments are reducible.
                return IsOpAssignment(NodeType);
            }
        }

        private static bool IsOpAssignment(ExpressionType op) {
            switch (op) {
                case ExpressionType.AddAssign:
                case ExpressionType.SubtractAssign:
                case ExpressionType.MultiplyAssign:
                case ExpressionType.AddAssignChecked:
                case ExpressionType.SubtractAssignChecked:
                case ExpressionType.MultiplyAssignChecked:
                case ExpressionType.DivideAssign:
                case ExpressionType.ModuloAssign:
                case ExpressionType.PowerAssign:
                case ExpressionType.AndAssign:
                case ExpressionType.OrAssign:
                case ExpressionType.RightShiftAssign:
                case ExpressionType.LeftShiftAssign:
                case ExpressionType.ExclusiveOrAssign:
                    return true;
            }
            return false;
        }

        public Expression Right {
            get { return _right; }
        }

        public Expression Left {
            get { return _left; }
        }

        public MethodInfo Method {
            get { return GetMethod(); }
        }

        internal virtual MethodInfo GetMethod() {
            return null;
        }

        public override Expression Reduce() {
            //Only reduce OpAssignment expressions.
            if (IsOpAssignment(NodeType)) {
                switch (_left.NodeType) {
                    case ExpressionType.MemberAccess:
                        return ReduceMember();

                    case ExpressionType.Index:
                        return ReduceIndex();

                    default:
                        return ReduceVariable();
                }
            }
            return this;
        }

        //Return the corresponding Op of an assignment op.
        private static ExpressionType GetBinaryOpFromAssignmentOp(ExpressionType op) {
            Debug.Assert(IsOpAssignment(op));
            switch (op) {
                case ExpressionType.AddAssign:
                    return ExpressionType.Add ;
                case ExpressionType.AddAssignChecked:
                    return ExpressionType.AddChecked;
                case ExpressionType.SubtractAssign:
                    return ExpressionType.Subtract;
                case ExpressionType.SubtractAssignChecked:
                    return ExpressionType.SubtractChecked;
                case ExpressionType.MultiplyAssign:
                    return ExpressionType.Multiply;
                case ExpressionType.MultiplyAssignChecked:
                    return ExpressionType.MultiplyChecked;
                case ExpressionType.DivideAssign:
                    return ExpressionType.Divide;
                case ExpressionType.ModuloAssign:
                    return ExpressionType.Modulo;
                case ExpressionType.PowerAssign:
                    return ExpressionType.Power;
                case ExpressionType.AndAssign:
                    return ExpressionType.And;
                case ExpressionType.OrAssign:
                    return ExpressionType.Or;
                case ExpressionType.RightShiftAssign:
                    return ExpressionType.RightShift;
                case ExpressionType.LeftShiftAssign:
                    return ExpressionType.LeftShift;
                case ExpressionType.ExclusiveOrAssign:
                    return ExpressionType.ExclusiveOr;
                default:
                    //must be an error
                    throw Error.InvalidOperation("op");
            }
            
        }

        private Expression ReduceVariable() {
            // v (op)= r
            // ... is reduced into ...
            // v = v (op) r
            var op = GetBinaryOpFromAssignmentOp(NodeType);
            return Expression.Assign(
                _left,
                Expression.MakeBinary(op, _left, _right, false, Method)
            );
        }

        private Expression ReduceMember() {
            MemberExpression member = (MemberExpression)_left;

            if (member.Expression == null) {
                //static member, reduce the same as variable
                return ReduceVariable();
            } else {
                // left.b (op)= r
                // ... is reduced into ...
                // temp1 = left
                // temp2 = temp1.b (op) r
                // temp1.b = temp2
                // temp2
                ParameterExpression temp1 = Variable(member.Expression.Type, "temp1");

                // 1. temp1 = left
                Expression e1 = Expression.Assign(temp1, member.Expression);

                // 2. temp2 = temp1.b (op) r
                var op = GetBinaryOpFromAssignmentOp(NodeType);
                Expression e2 = Expression.MakeBinary(op, Expression.MakeMemberAccess(temp1, member.Member), _right, false, Method);
                ParameterExpression temp2 = Variable(e2.Type, "temp2");
                e2 = Expression.Assign(temp2, e2);

                // 3. temp1.b = temp2
                Expression e3 = Expression.Assign(Expression.MakeMemberAccess(temp1, member.Member), temp2);

                // 3. temp2
                Expression e4 = temp2;

                return Expression.Block(
                    new ParameterExpression[] { temp1, temp2 },
                    e1, e2, e3, e4
                );
            }
        }

        private Expression ReduceIndex() {
            // left[a0, a1, ... aN] (op)= r
            //
            // ... is reduced into ...
            //
            // tempObj = left
            // tempArg0 = a0
            // ...
            // tempArgN = aN
            // tempValue = tempObj[tempArg0, ... tempArgN] (op) r
            // tempObj[tempArg0, ... tempArgN] = tempValue

            var index = (IndexExpression)_left;

            var vars = new List<ParameterExpression>(index.Arguments.Count + 2);
            var exprs = new List<Expression>(index.Arguments.Count + 3);

            var tempObj = Expression.Variable(index.Object.Type, "tempObj");
            vars.Add(tempObj);
            exprs.Add(Expression.Assign(tempObj, index.Object));

            var tempArgs = new List<Expression>(index.Arguments.Count);
            foreach (var arg in index.Arguments) {
                var tempArg = Expression.Variable(arg.Type, "tempArg" + tempArgs.Count);
                vars.Add(tempArg);
                tempArgs.Add(tempArg);
                exprs.Add(Expression.Assign(tempArg, arg));
            }

            var tempIndex = Expression.MakeIndex(tempObj, index.Indexer, tempArgs);

            // tempValue = tempObj[tempArg0, ... tempArgN] (op) r
            var binaryOp = GetBinaryOpFromAssignmentOp(NodeType);
            var op = Expression.MakeBinary(binaryOp, tempIndex, _right, false, Method);
            var tempValue = Expression.Variable(op.Type, "tempValue");
            vars.Add(tempValue);
            exprs.Add(Expression.Assign(tempValue, op));

            // tempObj[tempArg0, ... tempArgN] = tempValue
            exprs.Add(Expression.Assign(tempIndex, tempValue));

            return Expression.Block(vars, exprs);
        }

        public LambdaExpression Conversion {
            get { return GetConversion(); }
        }

        internal virtual LambdaExpression GetConversion() {
            return null;
        }

        public bool IsLifted {
            get {
                if (NodeType == ExpressionType.Coalesce || NodeType == ExpressionType.Assign) {
                    return false;
                }
                if (TypeUtils.IsNullableType(_left.Type)) {
                    MethodInfo method = GetMethod();
                    return method == null || method.GetParametersCached()[0].ParameterType != _left.Type;
                }
                return false;
            }
        }

        public bool IsLiftedToNull {
            get {
                return IsLifted && TypeUtils.IsNullableType(Type);
            }
        }

        internal override Expression Accept(ExpressionVisitor visitor) {
            return visitor.VisitBinary(this);
        }

        internal static Expression Create(ExpressionType nodeType, Expression left, Expression right, Type type, MethodInfo method, LambdaExpression conversion) {
            if (nodeType == ExpressionType.Assign) {
                Debug.Assert(method == null && type == left.Type);
                return new AssignBinaryExpression(left, right);
            }
            if (conversion != null) {
                Debug.Assert(method == null && type == right.Type && nodeType == ExpressionType.Coalesce);
                return new CoalesceConversionBinaryExpression(left, right, conversion);
            }
            if (method != null) {
                return new MethodBinaryExpression(nodeType, left, right, type, method);
            }
            if (type == typeof(bool)) {
                return new LogicalBinaryExpression(nodeType, left, right);
            }
            return new SimpleBinaryExpression(nodeType, left, right, type);
        }
    }

    // Optimized representation of simple logical expressions:
    // && || == != > < >= <=
    internal sealed class LogicalBinaryExpression : BinaryExpression {
        private readonly ExpressionType _nodeType;

        internal LogicalBinaryExpression(ExpressionType nodeType, Expression left, Expression right)
            : base(left, right) {
            _nodeType = nodeType;
        }

        protected override Type GetExpressionType() {
            return typeof(bool);
        }

        protected override ExpressionType GetNodeKind() {
            return _nodeType;
        }
    }

    // Optimized assignment node, only holds onto children
    internal sealed class AssignBinaryExpression : BinaryExpression {
        internal AssignBinaryExpression(Expression left, Expression right)
            : base(left, right) {
        }

        protected override Type GetExpressionType() {
            return Left.Type;
        }

        protected override ExpressionType GetNodeKind() {
            return ExpressionType.Assign;
        }
    }

    // Coalesce with conversion
    // This is not a frequently used node, but rather we want to save every
    // other BinaryExpression from holding onto the null conversion lambda
    internal sealed class CoalesceConversionBinaryExpression : BinaryExpression {
        private readonly LambdaExpression _conversion;

        internal CoalesceConversionBinaryExpression(Expression left, Expression right, LambdaExpression conversion)
            : base(left, right) {
            _conversion = conversion;
        }

        internal override LambdaExpression GetConversion() {
            return _conversion;
        }

        protected override ExpressionType GetNodeKind() {
            return ExpressionType.Coalesce;
        }

        protected override Type GetExpressionType() {
            return Right.Type;
        }
    }

    // Class that handles most binary expressions
    // If needed, it can be optimized even more (often Type == left.Type)
    internal class SimpleBinaryExpression : BinaryExpression {
        private readonly ExpressionType _nodeType;
        private readonly Type _type;

        internal SimpleBinaryExpression(ExpressionType nodeType, Expression left, Expression right, Type type)
            : base(left, right) {
            _nodeType = nodeType;
            _type = type;
        }

        protected override ExpressionType GetNodeKind() {
            return _nodeType;
        }

        protected override Type GetExpressionType() {
            return _type;
        }
    }

    // Class that handles binary expressions with a method
    // If needed, it can be optimized even more (often Type == method.ReturnType)
    internal sealed class MethodBinaryExpression : SimpleBinaryExpression {
        private readonly MethodInfo _method;

        internal MethodBinaryExpression(ExpressionType nodeType, Expression left, Expression right, Type type, MethodInfo method)
            : base(nodeType, left, right, type) {
            _method = method;
        }

        internal override MethodInfo GetMethod() {
            return _method;
        }
    }

    public partial class Expression {

        #region Assign

        /// <summary>
        /// Performs an assignment variable = value
        /// </summary>
        public static BinaryExpression Assign(Expression left, Expression right) {
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            TypeUtils.ValidateType(left.Type);
            TypeUtils.ValidateType(right.Type);
            if (!TypeUtils.AreReferenceAssignable(left.Type, right.Type)) {
                throw Error.ExpressionTypeDoesNotMatchAssignment(right.Type, left.Type);
            }
            return new AssignBinaryExpression(left, right);
        }

        #endregion

        //CONFORMING
        private static BinaryExpression GetUserDefinedBinaryOperator(ExpressionType binaryType, string name, Expression left, Expression right, bool liftToNull) {
            // try exact match first
            MethodInfo method = GetUserDefinedBinaryOperator(binaryType, left.Type, right.Type, name);
            if (method != null) {
                return new MethodBinaryExpression(binaryType, left, right, method.ReturnType, method);
            }
            // try lifted call
            if (TypeUtils.IsNullableType(left.Type) && TypeUtils.IsNullableType(right.Type)) {
                Type nnLeftType = TypeUtils.GetNonNullableType(left.Type);
                Type nnRightType = TypeUtils.GetNonNullableType(right.Type);
                method = GetUserDefinedBinaryOperator(binaryType, nnLeftType, nnRightType, name);
                if (method != null && method.ReturnType.IsValueType && !TypeUtils.IsNullableType(method.ReturnType)) {
                    if (method.ReturnType != typeof(bool) || liftToNull) {
                        return new MethodBinaryExpression(binaryType, left, right, TypeUtils.GetNullableType(method.ReturnType), method);
                    } else {
                        return new MethodBinaryExpression(binaryType, left, right, typeof(bool), method);
                    }
                }
            }
            return null;
        }

        //CONFORMING
        private static BinaryExpression GetMethodBasedBinaryOperator(ExpressionType binaryType, Expression left, Expression right, MethodInfo method, bool liftToNull) {
            System.Diagnostics.Debug.Assert(method != null);
            ValidateOperator(method);
            ParameterInfo[] pms = method.GetParametersCached();
            if (pms.Length != 2)
                throw Error.IncorrectNumberOfMethodCallArguments(method);
            if (ParameterIsAssignable(pms[0], left.Type) && ParameterIsAssignable(pms[1], right.Type)) {
                ValidateParamswithOperandsOrThrow(pms[0].ParameterType, left.Type, binaryType, method.Name);
                ValidateParamswithOperandsOrThrow(pms[1].ParameterType, right.Type, binaryType, method.Name);
                return new MethodBinaryExpression(binaryType, left, right, method.ReturnType, method);

            }
            // check for lifted call
            if (TypeUtils.IsNullableType(left.Type) && TypeUtils.IsNullableType(right.Type) &&
                ParameterIsAssignable(pms[0], TypeUtils.GetNonNullableType(left.Type)) &&
                ParameterIsAssignable(pms[1], TypeUtils.GetNonNullableType(right.Type)) &&
                method.ReturnType.IsValueType && !TypeUtils.IsNullableType(method.ReturnType)) {
                if (method.ReturnType != typeof(bool) || liftToNull) {
                    return new MethodBinaryExpression(binaryType, left, right, TypeUtils.GetNullableType(method.ReturnType), method);
                } else {
                    return new MethodBinaryExpression(binaryType, left, right, typeof(bool), method);
                }
            }
            throw Error.OperandTypesDoNotMatchParameters(binaryType, method.Name);
        }

        private static BinaryExpression GetMethodBasedAssignOperator(ExpressionType binaryType, Expression left, Expression right, MethodInfo method, bool liftToNull) {
            BinaryExpression b = GetMethodBasedBinaryOperator(binaryType, left, right, method, liftToNull);
            // return type must be assignable back to the left type
            if (!TypeUtils.AreReferenceAssignable(left.Type, b.Type)) {
                throw Error.UserDefinedOpMustHaveValidReturnType(binaryType, b.Method.Name);
            }
            return b;
        }

        //CONFORMING
        private static BinaryExpression GetUserDefinedBinaryOperatorOrThrow(ExpressionType binaryType, string name, Expression left, Expression right, bool liftToNull) {
            BinaryExpression b = GetUserDefinedBinaryOperator(binaryType, name, left, right, liftToNull);
            if (b != null) {
                ParameterInfo[] pis = b.Method.GetParametersCached();
                ValidateParamswithOperandsOrThrow(pis[0].ParameterType, left.Type, binaryType, name);
                ValidateParamswithOperandsOrThrow(pis[1].ParameterType, right.Type, binaryType, name);
                return b;
            }
            throw Error.BinaryOperatorNotDefined(binaryType, left.Type, right.Type);
        }

        private static BinaryExpression GetUserDefinedAssignOperatorOrThrow(ExpressionType binaryType, string name, Expression left, Expression right, bool liftToNull) {
            BinaryExpression b = GetUserDefinedBinaryOperatorOrThrow(binaryType, name, left, right, liftToNull);
            // return type must be assignable back to the left type
            if (!TypeUtils.AreReferenceAssignable(left.Type, b.Type)) {
                throw Error.UserDefinedOpMustHaveValidReturnType(binaryType, b.Method.Name);
            }
            return b;
        }
                
        //CONFORMING
        private static MethodInfo GetUserDefinedBinaryOperator(ExpressionType binaryType, Type leftType, Type rightType, string name) {
            // UNDONE: This algorithm is wrong, we should be checking for uniqueness and erroring if
            // UNDONE: it is defined on both types.
            Type[] types = new Type[] { leftType, rightType };
            Type nnLeftType = TypeUtils.GetNonNullableType(leftType);
            Type nnRightType = TypeUtils.GetNonNullableType(rightType);
            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo method = nnLeftType.GetMethod(name, flags, null, types, null);
            if (method == null && leftType != rightType) {
                method = nnRightType.GetMethod(name, flags, null, types, null);
            }

            if (IsLiftingConditionalLogicalOperator(leftType, rightType, method, binaryType)) {
                method = GetUserDefinedBinaryOperator(binaryType, nnLeftType, nnRightType, name);
            }
            return method;
        }

        //CONFORMING
        private static bool IsLiftingConditionalLogicalOperator(Type left, Type right, MethodInfo method, ExpressionType binaryType) {
            return TypeUtils.IsNullableType(right) &&
                    TypeUtils.IsNullableType(left) &&
                    method == null &&
                    (binaryType == ExpressionType.AndAlso || binaryType == ExpressionType.OrElse);
        }

        //CONFORMING
        private static bool ParameterIsAssignable(ParameterInfo pi, Type argType) {
            Type pType = pi.ParameterType;
            if (pType.IsByRef)
                pType = pType.GetElementType();
            return TypeUtils.AreReferenceAssignable(pType, argType);
        }

        //CONFORMING
        private static void ValidateParamswithOperandsOrThrow(Type paramType, Type operandType, ExpressionType exprType, string name) {
            if (TypeUtils.IsNullableType(paramType) && !TypeUtils.IsNullableType(operandType)) {
                throw Error.OperandTypesDoNotMatchParameters(exprType, name);
            }
        }

        //CONFORMING
        private static void ValidateOperator(MethodInfo method) {
            System.Diagnostics.Debug.Assert(method != null);
            ValidateMethodInfo(method);
            if (!method.IsStatic)
                throw Error.UserDefinedOperatorMustBeStatic(method);
            if (method.ReturnType == typeof(void))
                throw Error.UserDefinedOperatorMustNotBeVoid(method);
        }

        //TODO: consider moving to utils. It is used in many places.
        //CONFORMING
        private static void ValidateMethodInfo(MethodInfo method) {
            if (method.IsGenericMethodDefinition)
                throw Error.MethodIsGeneric(method);
            if (method.ContainsGenericParameters)
                throw Error.MethodContainsGenericParameters(method);
        }

        //CONFORMING
        private static bool IsNullComparison(Expression left, Expression right) {
            // If we have x==null, x!=null, null==x or null!=x where x is
            // nullable but not null, then this is treated as a call to x.HasValue
            // and is legal even if there is no equality operator defined on the
            // type of x.
            if (IsNullConstant(left) && !IsNullConstant(right) && TypeUtils.IsNullableType(right.Type)) {
                return true;
            }
            if (IsNullConstant(right) && !IsNullConstant(left) && TypeUtils.IsNullableType(left.Type)) {
                return true;
            }
            return false;
        }

        //CONFORMING
        // Note: this has different meaning than ConstantCheck.IsNull
        // That function attempts to determine if the result of a tree will be
        // null at runtime. This function is used at tree construction time and
        // only looks for a ConstantExpression with a null Value. It can't
        // become "smarter" or that would break tree construction.
        private static bool IsNullConstant(Expression e) {
            var c = e as ConstantExpression;
            return c != null && c.Value == null;
        }

        //CONFORMING
        private static void ValidateUserDefinedConditionalLogicOperator(ExpressionType nodeType, Type left, Type right, MethodInfo method) {
            ValidateOperator(method);
            ParameterInfo[] pms = method.GetParametersCached();
            if (pms.Length != 2)
                throw Error.IncorrectNumberOfMethodCallArguments(method);
            if (!ParameterIsAssignable(pms[0], left)) {
                if (!(TypeUtils.IsNullableType(left) && ParameterIsAssignable(pms[0], TypeUtils.GetNonNullableType(left))))
                    throw Error.OperandTypesDoNotMatchParameters(nodeType, method.Name);
            }
            if (!ParameterIsAssignable(pms[1], right)) {
                if (!(TypeUtils.IsNullableType(right) && ParameterIsAssignable(pms[1], TypeUtils.GetNonNullableType(right))))
                    throw Error.OperandTypesDoNotMatchParameters(nodeType, method.Name);
            }
            if (pms[0].ParameterType != pms[1].ParameterType)
                throw Error.UserDefinedOpMustHaveConsistentTypes(nodeType, method.Name);
            if (method.ReturnType != pms[0].ParameterType)
                throw Error.UserDefinedOpMustHaveConsistentTypes(nodeType, method.Name);
            if (IsValidLiftedConditionalLogicalOperator(left, right, pms)) {
                left = TypeUtils.GetNonNullableType(left);
                right = TypeUtils.GetNonNullableType(left);
            }
            MethodInfo opTrue = TypeUtils.GetBooleanOperator(method.DeclaringType, "op_True");
            MethodInfo opFalse = TypeUtils.GetBooleanOperator(method.DeclaringType, "op_False");
            if (opTrue == null || opTrue.ReturnType != typeof(bool) ||
                opFalse == null || opFalse.ReturnType != typeof(bool)) {
                throw Error.LogicalOperatorMustHaveBooleanOperators(nodeType, method.Name);
            }
        }

        //CONFORMING
        private static bool IsValidLiftedConditionalLogicalOperator(Type left, Type right, ParameterInfo[] pms) {
            return left == right && TypeUtils.IsNullableType(right) && pms[1].ParameterType == TypeUtils.GetNonNullableType(right);
        }

        //CONFORMING
        public static BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right) {
            return MakeBinary(binaryType, left, right, false, null, null);
        }
        //CONFORMING
        public static BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right, bool liftToNull, MethodInfo method) {
            return MakeBinary(binaryType, left, right, liftToNull, method, null);
        }
        //CONFORMING
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static BinaryExpression MakeBinary(ExpressionType binaryType, Expression left, Expression right, bool liftToNull, MethodInfo method, LambdaExpression conversion) {
            switch (binaryType) {
                case ExpressionType.Add:
                    return Add(left, right, method);
                case ExpressionType.AddChecked:
                    return AddChecked(left, right, method);
                case ExpressionType.Subtract:
                    return Subtract(left, right, method);
                case ExpressionType.SubtractChecked:
                    return SubtractChecked(left, right, method);
                case ExpressionType.Multiply:
                    return Multiply(left, right, method);
                case ExpressionType.MultiplyChecked:
                    return MultiplyChecked(left, right, method);
                case ExpressionType.Divide:
                    return Divide(left, right, method);
                case ExpressionType.Modulo:
                    return Modulo(left, right, method);
                case ExpressionType.Power:
                    return Power(left, right, method);
                case ExpressionType.And:
                    return And(left, right, method);
                case ExpressionType.AndAlso:
                    return AndAlso(left, right, method);
                case ExpressionType.Or:
                    return Or(left, right, method);
                case ExpressionType.OrElse:
                    return OrElse(left, right, method);
                case ExpressionType.LessThan:
                    return LessThan(left, right, liftToNull, method);
                case ExpressionType.LessThanOrEqual:
                    return LessThanOrEqual(left, right, liftToNull, method);
                case ExpressionType.GreaterThan:
                    return GreaterThan(left, right, liftToNull, method);
                case ExpressionType.GreaterThanOrEqual:
                    return GreaterThanOrEqual(left, right, liftToNull, method);
                case ExpressionType.Equal:
                    return Equal(left, right, liftToNull, method);
                case ExpressionType.NotEqual:
                    return NotEqual(left, right, liftToNull, method);
                case ExpressionType.ExclusiveOr:
                    return ExclusiveOr(left, right, method);
                case ExpressionType.Coalesce:
                    return Coalesce(left, right, conversion);
                case ExpressionType.ArrayIndex:
                    return ArrayIndex(left, right);
                case ExpressionType.RightShift:
                    return RightShift(left, right, method);
                case ExpressionType.LeftShift:
                    return LeftShift(left, right, method);
                case ExpressionType.Assign:
                    return Assign(left, right);
                case ExpressionType.AddAssign:
                    return AddAssign(left, right, method);
                case ExpressionType.AndAssign:
                    return AndAssign(left, right, method);
                case ExpressionType.DivideAssign:
                    return DivideAssign(left, right, method);
                case ExpressionType.ExclusiveOrAssign:
                    return ExclusiveOrAssign(left, right, method);
                case ExpressionType.LeftShiftAssign:
                    return LeftShiftAssign(left, right, method);
                case ExpressionType.ModuloAssign:
                    return ModuloAssign(left, right, method);
                case ExpressionType.MultiplyAssign:
                    return MultiplyAssign(left, right, method);
                case ExpressionType.OrAssign:
                    return OrAssign(left, right, method);
                case ExpressionType.PowerAssign:
                    return PowerAssign(left, right, method);
                case ExpressionType.RightShiftAssign:
                    return RightShiftAssign(left, right, method);
                case ExpressionType.SubtractAssign:
                    return SubtractAssign(left, right, method);
                case ExpressionType.AddAssignChecked:
                    return AddAssignChecked(left, right, method);
                case ExpressionType.SubtractAssignChecked:
                    return SubtractAssignChecked(left, right, method);
                case ExpressionType.MultiplyAssignChecked:
                    return MultiplyAssignChecked(left, right, method);
                default:
                    throw Error.UnhandledBinary(binaryType);
            }
        }

        #region Equality Operators

        //CONFORMING
        public static BinaryExpression Equal(Expression left, Expression right) {
            return Equal(left, right, false, null);
        }
        //CONFORMING
        public static BinaryExpression Equal(Expression left, Expression right, bool liftToNull, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                return GetEqualityComparisonOperator(ExpressionType.Equal, "op_Equality", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Equal, left, right, method, liftToNull);
        }

        //CONFORMING
        public static BinaryExpression NotEqual(Expression left, Expression right) {
            return NotEqual(left, right, false, null);
        }
        //CONFORMING
        public static BinaryExpression NotEqual(Expression left, Expression right, bool liftToNull, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                return GetEqualityComparisonOperator(ExpressionType.NotEqual, "op_Inequality", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.NotEqual, left, right, method, liftToNull);
        }

        //CONFORMING
        private static BinaryExpression GetEqualityComparisonOperator(ExpressionType binaryType, string opName, Expression left, Expression right, bool liftToNull) {
            // known comparison - numeric types, bools, object, enums
            if (left.Type == right.Type && (TypeUtils.IsNumeric(left.Type) || 
                left.Type == typeof(object) || 
                TypeUtils.IsBool(left.Type) || 
                TypeUtils.GetNonNullableType(left.Type).IsEnum)) {
                if (TypeUtils.IsNullableType(left.Type) && liftToNull) {
                    return new SimpleBinaryExpression(binaryType, left, right, typeof(bool?));
                } else {
                    return new LogicalBinaryExpression(binaryType, left, right);
                }
            }
            // look for user defined operator
            BinaryExpression b = GetUserDefinedBinaryOperator(binaryType, opName, left, right, liftToNull);
            if (b != null) {
                return b;
            }
            if (TypeUtils.HasBuiltInEqualityOperator(left.Type, right.Type) || IsNullComparison(left, right)) {
                if (TypeUtils.IsNullableType(left.Type) && liftToNull) {
                    return new SimpleBinaryExpression(binaryType, left, right, typeof(bool?));
                } else {
                    return new LogicalBinaryExpression(binaryType, left, right);
                }
            }
            throw Error.BinaryOperatorNotDefined(binaryType, left.Type, right.Type);
        }

        #endregion

        #region Comparison Expressions

        //CONFORMING
        public static BinaryExpression GreaterThan(Expression left, Expression right) {
            return GreaterThan(left, right, false, null);
        }
        //CONFORMING
        public static BinaryExpression GreaterThan(Expression left, Expression right, bool liftToNull, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                return GetComparisonOperator(ExpressionType.GreaterThan, "op_GreaterThan", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.GreaterThan, left, right, method, liftToNull);
        }

        //CONFORMING
        public static BinaryExpression LessThan(Expression left, Expression right) {
            return LessThan(left, right, false, null);
        }
        //CONFORMING
        public static BinaryExpression LessThan(Expression left, Expression right, bool liftToNull, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                return GetComparisonOperator(ExpressionType.LessThan, "op_LessThan", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.LessThan, left, right, method, liftToNull);
        }

        //CONFORMING
        public static BinaryExpression GreaterThanOrEqual(Expression left, Expression right) {
            return GreaterThanOrEqual(left, right, false, null);
        }
        //CONFORMING
        public static BinaryExpression GreaterThanOrEqual(Expression left, Expression right, bool liftToNull, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                return GetComparisonOperator(ExpressionType.GreaterThanOrEqual, "op_GreaterThanOrEqual", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.GreaterThanOrEqual, left, right, method, liftToNull);
        }

        //CONFORMING
        public static BinaryExpression LessThanOrEqual(Expression left, Expression right) {
            return LessThanOrEqual(left, right, false, null);
        }
        //CONFORMING
        public static BinaryExpression LessThanOrEqual(Expression left, Expression right, bool liftToNull, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                return GetComparisonOperator(ExpressionType.LessThanOrEqual, "op_LessThanOrEqual", left, right, liftToNull);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.LessThanOrEqual, left, right, method, liftToNull);
        }

        //CONFORMING
        private static BinaryExpression GetComparisonOperator(ExpressionType binaryType, string opName, Expression left, Expression right, bool liftToNull) {
            if (left.Type == right.Type && TypeUtils.IsNumeric(left.Type)) {
                if (TypeUtils.IsNullableType(left.Type) && liftToNull) {
                    return new SimpleBinaryExpression(binaryType, left, right, typeof(bool?));
                } else {
                    return new LogicalBinaryExpression(binaryType, left, right);
                }
            }
            return GetUserDefinedBinaryOperatorOrThrow(binaryType, opName, left, right, liftToNull);
        }

        #endregion

        #region Boolean Expressions

        //CONFORMING
        public static BinaryExpression AndAlso(Expression left, Expression right) {
            return AndAlso(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression AndAlso(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            Type returnType;
            if (method == null) {
                if (left.Type == right.Type) {
                    if (left.Type == typeof(bool)) {
                        return new LogicalBinaryExpression(ExpressionType.AndAlso, left, right);
                    } else if (left.Type == typeof(bool?)) {
                        return new SimpleBinaryExpression(ExpressionType.AndAlso, left, right, left.Type);
                    }
                }
                method = GetUserDefinedBinaryOperator(ExpressionType.AndAlso, left.Type, right.Type, "op_BitwiseAnd");
                if (method != null) {
                    ValidateUserDefinedConditionalLogicOperator(ExpressionType.AndAlso, left.Type, right.Type, method);
                    returnType = (TypeUtils.IsNullableType(left.Type) && method.ReturnType == TypeUtils.GetNonNullableType(left.Type)) ? left.Type : method.ReturnType;
                    return new MethodBinaryExpression(ExpressionType.AndAlso, left, right, returnType, method);
                }
                throw Error.BinaryOperatorNotDefined(ExpressionType.AndAlso, left.Type, right.Type);
            }
            ValidateUserDefinedConditionalLogicOperator(ExpressionType.AndAlso, left.Type, right.Type, method);
            returnType = (TypeUtils.IsNullableType(left.Type) && method.ReturnType == TypeUtils.GetNonNullableType(left.Type)) ? left.Type : method.ReturnType;
            return new MethodBinaryExpression(ExpressionType.AndAlso, left, right, returnType, method);
        }

        //CONFORMING
        public static BinaryExpression OrElse(Expression left, Expression right) {
            return OrElse(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression OrElse(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            Type returnType;
            if (method == null) {
                if (left.Type == right.Type) {
                    if (left.Type == typeof(bool)) {
                        return new LogicalBinaryExpression(ExpressionType.OrElse, left, right);
                    } else if (left.Type == typeof(bool?)) {
                        return new SimpleBinaryExpression(ExpressionType.OrElse, left, right, left.Type);
                    }
                }
                method = GetUserDefinedBinaryOperator(ExpressionType.OrElse, left.Type, right.Type, "op_BitwiseOr");
                if (method != null) {
                    ValidateUserDefinedConditionalLogicOperator(ExpressionType.OrElse, left.Type, right.Type, method);
                    returnType = (TypeUtils.IsNullableType(left.Type) && method.ReturnType == TypeUtils.GetNonNullableType(left.Type)) ? left.Type : method.ReturnType;
                    return new MethodBinaryExpression(ExpressionType.OrElse, left, right, returnType, method);
                }
                throw Error.BinaryOperatorNotDefined(ExpressionType.OrElse, left.Type, right.Type);
            }
            ValidateUserDefinedConditionalLogicOperator(ExpressionType.OrElse, left.Type, right.Type, method);
            returnType = (TypeUtils.IsNullableType(left.Type) && method.ReturnType == TypeUtils.GetNonNullableType(left.Type)) ? left.Type : method.ReturnType;
            return new MethodBinaryExpression(ExpressionType.OrElse, left, right, returnType, method);
        }

        #endregion

        #region Coalescing Expressions

        //CONFORMING
        public static BinaryExpression Coalesce(Expression left, Expression right) {
            return Coalesce(left, right, null);
        }

        //CONFORMING
        public static BinaryExpression Coalesce(Expression left, Expression right, LambdaExpression conversion) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");

            if (conversion == null) {
                Type resultType = ValidateCoalesceArgTypes(left.Type, right.Type);
                return new SimpleBinaryExpression(ExpressionType.Coalesce, left, right, resultType);
            }

            if (left.Type.IsValueType && !TypeUtils.IsNullableType(left.Type)) {
                throw Error.CoalesceUsedOnNonNullType();
            }

            Type delegateType = conversion.Type;
            Debug.Assert(typeof(System.Delegate).IsAssignableFrom(delegateType) && delegateType != typeof(System.Delegate));
            MethodInfo method = delegateType.GetMethod("Invoke");
            if (method.ReturnType == typeof(void))
                throw Error.UserDefinedOperatorMustNotBeVoid(conversion);
            ParameterInfo[] pms = method.GetParametersCached();
            Debug.Assert(pms.Length == conversion.Parameters.Count);
            if (pms.Length != 1)
                throw Error.IncorrectNumberOfMethodCallArguments(conversion);
            // The return type must match exactly.
            // CONSIDER: We could weaken this restriction and
            // CONSIDER: say that the return type must be assignable to from
            // CONSIDER: the return type of the lambda.
            if (method.ReturnType != right.Type) {
                throw Error.OperandTypesDoNotMatchParameters(ExpressionType.Coalesce, conversion.ToString());
            }
            // The parameter of the conversion lambda must either be assignable
            // from the erased or unerased type of the left hand side.
            if (!ParameterIsAssignable(pms[0], TypeUtils.GetNonNullableType(left.Type)) &&
                !ParameterIsAssignable(pms[0], left.Type)) {
                throw Error.OperandTypesDoNotMatchParameters(ExpressionType.Coalesce, conversion.ToString());
            }
            return new CoalesceConversionBinaryExpression(left, right, conversion);
        }

        //CONFORMING
        private static Type ValidateCoalesceArgTypes(Type left, Type right) {
            Type leftStripped = TypeUtils.GetNonNullableType(left);
            if (left.IsValueType && !TypeUtils.IsNullableType(left)) {
                throw Error.CoalesceUsedOnNonNullType();
            } else if (TypeUtils.IsNullableType(left) && TypeUtils.IsImplicitlyConvertible(right, leftStripped)) {
                return leftStripped;
            } else if (TypeUtils.IsImplicitlyConvertible(right, left)) {
                return left;
            } else if (TypeUtils.IsImplicitlyConvertible(leftStripped, right)) {
                return right;
            } else {
                throw Error.ArgumentTypesMustMatch();
            }
        }



        #endregion

        #region Arithmetic Expressions

        //CONFORMING
        public static BinaryExpression Add(Expression left, Expression right) {
            return Add(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression Add(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.Add, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Add, "op_Addition", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Add, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression AddAssign(Expression left, Expression right) {
            return AddAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression AddAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.AddAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.AddAssign, "op_Addition", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.AddAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression AddAssignChecked(Expression left, Expression right) {
            return AddAssignChecked(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression AddAssignChecked(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.AddAssignChecked, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.AddAssignChecked, "op_Addition", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.AddAssignChecked, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression AddChecked(Expression left, Expression right) {
            return AddChecked(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression AddChecked(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.AddChecked, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.AddChecked, "op_Addition", left, right, false);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.AddChecked, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression Subtract(Expression left, Expression right) {
            return Subtract(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression Subtract(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.Subtract, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Subtract, "op_Subtraction", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Subtract, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression SubtractAssign(Expression left, Expression right) {
            return SubtractAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression SubtractAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.SubtractAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.SubtractAssign, "op_Subtraction", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.SubtractAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression SubtractAssignChecked(Expression left, Expression right) {
            return SubtractAssignChecked(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression SubtractAssignChecked(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.SubtractAssignChecked, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.SubtractAssignChecked, "op_Subtraction", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.SubtractAssignChecked, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression SubtractChecked(Expression left, Expression right) {
            return SubtractChecked(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression SubtractChecked(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.SubtractChecked, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.SubtractChecked, "op_Subtraction", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.SubtractChecked, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression Divide(Expression left, Expression right) {
            return Divide(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression Divide(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.Divide, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Divide, "op_Division", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Divide, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression DivideAssign(Expression left, Expression right) {
            return DivideAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression DivideAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.DivideAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.DivideAssign, "op_Division", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.DivideAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression Modulo(Expression left, Expression right) {
            return Modulo(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression Modulo(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.Modulo, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Modulo, "op_Modulus", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Modulo, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression ModuloAssign(Expression left, Expression right) {
            return ModuloAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression ModuloAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.ModuloAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.ModuloAssign, "op_Modulus", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.ModuloAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression Multiply(Expression left, Expression right) {
            return Multiply(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression Multiply(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.Multiply, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Multiply, "op_Multiply", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Multiply, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression MultiplyAssign(Expression left, Expression right) {
            return MultiplyAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression MultiplyAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.MultiplyAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.MultiplyAssign, "op_Multiply", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.MultiplyAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression MultiplyAssignChecked(Expression left, Expression right) {
            return MultiplyAssignChecked(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression MultiplyAssignChecked(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.MultiplyAssignChecked, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.MultiplyAssignChecked, "op_Multiply", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.MultiplyAssignChecked, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression MultiplyChecked(Expression left, Expression right) {
            return MultiplyChecked(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression MultiplyChecked(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsArithmetic(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.MultiplyChecked, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.MultiplyChecked, "op_Multiply", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.MultiplyChecked, left, right, method, true);
        }

        private static bool IsSimpleShift(Type left, Type right) {
            return TypeUtils.IsInteger(left)
                && TypeUtils.GetNonNullableType(right) == typeof(int)
                && (left.IsNullableType() == right.IsNullableType());
        }

        //CONFORMING
        public static BinaryExpression LeftShift(Expression left, Expression right) {
            return LeftShift(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression LeftShift(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (IsSimpleShift(left.Type, right.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.LeftShift, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.LeftShift, "op_LeftShift", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.LeftShift, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression LeftShiftAssign(Expression left, Expression right) {
            return LeftShiftAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression LeftShiftAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (IsSimpleShift(left.Type, right.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.LeftShiftAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.LeftShiftAssign, "op_LeftShift", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.LeftShiftAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression RightShift(Expression left, Expression right) {
            return RightShift(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression RightShift(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (IsSimpleShift(left.Type, right.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.RightShift, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.RightShift, "op_RightShift", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.RightShift, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression RightShiftAssign(Expression left, Expression right) {
            return RightShiftAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression RightShiftAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (IsSimpleShift(left.Type, right.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.RightShiftAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.RightShiftAssign, "op_RightShift", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.RightShiftAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression And(Expression left, Expression right) {
            return And(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression And(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsIntegerOrBool(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.And, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.And, "op_BitwiseAnd", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.And, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression AndAssign(Expression left, Expression right) {
            return AndAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression AndAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsIntegerOrBool(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.AndAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.AndAssign, "op_BitwiseAnd", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.AndAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression Or(Expression left, Expression right) {
            return Or(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression Or(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsIntegerOrBool(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.Or, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.Or, "op_BitwiseOr", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Or, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression OrAssign(Expression left, Expression right) {
            return OrAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression OrAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsIntegerOrBool(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.OrAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.OrAssign, "op_BitwiseOr", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.OrAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression ExclusiveOr(Expression left, Expression right) {
            return ExclusiveOr(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression ExclusiveOr(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsIntegerOrBool(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.ExclusiveOr, left, right, left.Type);
                }
                return GetUserDefinedBinaryOperatorOrThrow(ExpressionType.ExclusiveOr, "op_ExclusiveOr", left, right, true);
            }
            return GetMethodBasedBinaryOperator(ExpressionType.ExclusiveOr, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression ExclusiveOrAssign(Expression left, Expression right) {
            return ExclusiveOrAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression ExclusiveOrAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                if (left.Type == right.Type && TypeUtils.IsIntegerOrBool(left.Type)) {
                    return new SimpleBinaryExpression(ExpressionType.ExclusiveOrAssign, left, right, left.Type);
                }
                return GetUserDefinedAssignOperatorOrThrow(ExpressionType.ExclusiveOrAssign, "op_ExclusiveOr", left, right, true);
            }
            return GetMethodBasedAssignOperator(ExpressionType.ExclusiveOrAssign, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression Power(Expression left, Expression right) {
            return Power(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression Power(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                Type mathType = typeof(System.Math);
                method = mathType.GetMethod("Pow", BindingFlags.Static | BindingFlags.Public);
                if (method == null) {
                    throw Error.BinaryOperatorNotDefined(ExpressionType.Power, left.Type, right.Type);
                }
            }
            return GetMethodBasedBinaryOperator(ExpressionType.Power, left, right, method, true);
        }

        //CONFORMING
        public static BinaryExpression PowerAssign(Expression left, Expression right) {
            return PowerAssign(left, right, null);
        }
        //CONFORMING
        public static BinaryExpression PowerAssign(Expression left, Expression right, MethodInfo method) {
            RequiresCanRead(left, "left");
            RequiresCanWrite(left, "left");
            RequiresCanRead(right, "right");
            if (method == null) {
                Type mathType = typeof(System.Math);
                method = mathType.GetMethod("Pow", BindingFlags.Static | BindingFlags.Public);
                if (method == null) {
                    throw Error.BinaryOperatorNotDefined(ExpressionType.PowerAssign, left.Type, right.Type);
                }
            }
            return GetMethodBasedAssignOperator(ExpressionType.PowerAssign, left, right, method, true);
        }

        #endregion

        #region ArrayIndex Expression

        //CONFORMING
        public static BinaryExpression ArrayIndex(Expression array, Expression index) {
            RequiresCanRead(array, "array");
            RequiresCanRead(index, "index");
            if (index.Type != typeof(int))
                throw Error.ArgumentMustBeArrayIndexType();

            Type arrayType = array.Type;
            if (!arrayType.IsArray)
                throw Error.ArgumentMustBeArray();
            if (arrayType.GetArrayRank() != 1)
                throw Error.IncorrectNumberOfIndexes();

            return new SimpleBinaryExpression(ExpressionType.ArrayIndex, array, index, arrayType.GetElementType());
        }

        #endregion        
    }
}
