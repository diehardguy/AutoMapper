using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AutoMapper.Impl;
using AutoMapper.Internal;

namespace AutoMapper
{
    public class PropertyMap
    {
        private readonly LinkedList<IValueResolver> _sourceValueResolvers = new LinkedList<IValueResolver>();
        private readonly IList<Type> _valueFormattersToSkip = new List<Type>();
        private readonly IList<IValueFormatter> _valueFormatters = new List<IValueFormatter>();
        private bool _ignored;
        private int _mappingOrder;
        private bool _hasCustomValueResolver;
        private IValueResolver _customResolver;
        private IValueResolver _customMemberResolver;
        private object _nullSubstitute;
        private bool _sealed;
        private IValueResolver[] _cachedResolvers;
        private Func<ResolutionContext, bool> _condition;
        private MemberInfo _sourceMember;
        private bool _ignoreProjectionUnlessExpanded;

        public PropertyMap(IMemberAccessor destinationProperty)
        {
            UseDestinationValue = true; 
            DestinationProperty = destinationProperty;
        }

        public PropertyMap(PropertyMap inheritedMappedProperty)
            : this(inheritedMappedProperty.DestinationProperty)
        {
            if (inheritedMappedProperty.IsIgnored())
                Ignore();
            else
            {
                foreach (var sourceValueResolver in inheritedMappedProperty.GetSourceValueResolvers())
                {
                    ChainResolver(sourceValueResolver);
                }
            }
            ApplyCondition(inheritedMappedProperty._condition);
            SetNullSubstitute(inheritedMappedProperty._nullSubstitute);
            SetMappingOrder(inheritedMappedProperty._mappingOrder);
        }

        public IMemberAccessor DestinationProperty { get; private set; }
        public Type DestinationPropertyType { get { return DestinationProperty.MemberType; } }
        public LambdaExpression CustomExpression { get; private set; }

        public bool IgnoreProjectionUnlessExpanded
        {
            get { return _ignoreProjectionUnlessExpanded; }
            set { _ignoreProjectionUnlessExpanded = value; }
        }

        public MemberInfo SourceMember
        {
            get
            {
                if (_sourceMember == null)
                {
                    var sourceMemberGetter = GetSourceValueResolvers()
                        .OfType<IMemberGetter>().LastOrDefault();
                    return sourceMemberGetter == null ? null : sourceMemberGetter.MemberInfo;
                }
                else
                {
                    return _sourceMember;
                }
            }
            internal set
            {
                _sourceMember = value;
            }
        }

        public bool CanBeSet
        {
            get
            {
                return !(DestinationProperty is PropertyAccessor) ||
                       ((PropertyAccessor)DestinationProperty).HasSetter;
            }
        }

        public bool UseDestinationValue { get; set; }

        internal bool HasCustomValueResolver
        {
            get { return _hasCustomValueResolver; }
        }

        public IEnumerable<IValueResolver> GetSourceValueResolvers()
        {
            if (_customMemberResolver != null)
                yield return _customMemberResolver;

            if (_customResolver != null)
                yield return _customResolver;

            foreach (var resolver in _sourceValueResolvers)
            {
                yield return resolver;
            }

            if (_nullSubstitute != null)
                yield return new NullReplacementMethod(_nullSubstitute);
        }

        public void RemoveLastResolver()
        {
            _sourceValueResolvers.RemoveLast();
        }

        public ResolutionResult ResolveValue(ResolutionContext context)
        {
            Seal();

            var result = new ResolutionResult(context);

            return _cachedResolvers.Aggregate(result, (current, resolver) => resolver.Resolve(current));
        }

        internal void Seal()
        {
            if (_sealed)
            {
                return;
            }

            _cachedResolvers = GetSourceValueResolvers().ToArray();
            _sealed = true;
        }

        public void ChainResolver(IValueResolver IValueResolver)
        {
            _sourceValueResolvers.AddLast(IValueResolver);
        }

        public void AddFormatterToSkip<TValueFormatter>() where TValueFormatter : IValueFormatter
        {
            _valueFormattersToSkip.Add(typeof(TValueFormatter));
        }

        public bool FormattersToSkipContains(Type valueFormatterType)
        {
            return _valueFormattersToSkip.Contains(valueFormatterType);
        }

        public void AddFormatter(IValueFormatter valueFormatter)
        {
            _valueFormatters.Add(valueFormatter);
        }

        public IValueFormatter[] GetFormatters()
        {
            return _valueFormatters.ToArray();
        }

        public void AssignCustomValueResolver(IValueResolver valueResolver)
        {
            _ignored = false;
            _customResolver = valueResolver;
            ResetSourceMemberChain();
            _hasCustomValueResolver = true;
        }

        public void ChainTypeMemberForResolver(IValueResolver valueResolver)
        {
            ResetSourceMemberChain();
            _customMemberResolver = valueResolver;
        }

        public void ChainConstructorForResolver(IValueResolver valueResolver)
        {
            _customResolver = valueResolver;
        }

        public void Ignore()
        {
            _ignored = true;
        }

        public bool IsIgnored()
        {
            return _ignored;
        }

        public void SetMappingOrder(int mappingOrder)
        {
            _mappingOrder = mappingOrder;
        }

        public int GetMappingOrder()
        {
            return _mappingOrder;
        }

        public bool IsMapped()
        {
            return _sourceValueResolvers.Count > 0 || _hasCustomValueResolver || _ignored;
        }

        public bool CanResolveValue()
        {
            return (_sourceValueResolvers.Count > 0 || _hasCustomValueResolver || UseDestinationValue) && !_ignored;
        }

        public void RemoveLastFormatter()
        {
            _valueFormatters.RemoveAt(_valueFormatters.Count - 1);
        }

        public void SetNullSubstitute(object nullSubstitute)
        {
            _nullSubstitute = nullSubstitute;
        }

        private void ResetSourceMemberChain()
        {
            _sourceValueResolvers.Clear();
        }

        public bool Equals(PropertyMap other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(other.DestinationProperty, DestinationProperty);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != typeof(PropertyMap)) return false;
            return Equals((PropertyMap)obj);
        }

        public override int GetHashCode()
        {
            return DestinationProperty.GetHashCode();
        }

        public void ApplyCondition(Func<ResolutionContext, bool> condition)
        {
            _condition = condition;
        }

        public bool ShouldAssignValue(ResolutionContext context)
        {
            return _condition == null || _condition(context);
        }

        public void SetCustomValueResolverExpression<TSource, TMember>(Expression<Func<TSource, TMember>> sourceMember)
        {
            if (sourceMember.Body is MemberExpression)
            {
                SourceMember = ((MemberExpression)sourceMember.Body).Member;
            }
            CustomExpression = sourceMember;
            AssignCustomValueResolver(
                new NullReferenceExceptionSwallowingResolver(
                    new DelegateBasedResolver<TSource, TMember>(sourceMember.Compile())
                )
            );
        }

        public object GetDestinationValue(object mappedObject)
        {
		     return UseDestinationValue
                ? DestinationProperty.GetValue(mappedObject)
                : null;
        }

        public ExpressionResolutionResult ResolveExpression(Type currentType, Expression instanceParameter)
        {
            Expression currentChild = instanceParameter;
            Type currentChildType = currentType;
            foreach (var resolver in GetSourceValueResolvers())
            {
                var getter = resolver as IMemberGetter;
                if (getter != null)
                {
                    var memberInfo = getter.MemberInfo;

                    var propertyInfo = memberInfo as PropertyInfo;
                    if (propertyInfo != null)
                    {
                        currentChild = Expression.Property(currentChild, propertyInfo);
                        currentChildType = propertyInfo.PropertyType;
                    }
                    else
                    {
                        throw new NotImplementedException("Expressions mapping from methods not supported yet.");
                    }
                }
                else
                {
                    var oldParameter = CustomExpression.Parameters.Single();
                    var newParameter = instanceParameter;
                    var converter = new ConversionVisitor(newParameter, oldParameter);
                    
                    currentChild = converter.Visit(CustomExpression.Body);
                    currentChildType = currentChild.Type;
                }
            }

            return new ExpressionResolutionResult(currentChild, currentChildType);
        }

        /// <summary>
        /// This expression visitor will replace an input parameter by another one
        /// 
        /// see http://stackoverflow.com/questions/4601844/expression-tree-copy-or-convert
        /// </summary>
        private class ConversionVisitor : ExpressionVisitor
        {
            private readonly Expression newParameter;
            private readonly ParameterExpression oldParameter;

            public ConversionVisitor(Expression newParameter, ParameterExpression oldParameter)
            {
                this.newParameter = newParameter;
                this.oldParameter = oldParameter;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                // replace all old param references with new ones
                return node.Type == oldParameter.Type ? newParameter : node;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                if (node.Expression != oldParameter) // if instance is not old parameter - do nothing
                    return base.VisitMember(node);

                var newObj = Visit(node.Expression);
                var newMember = newParameter.Type.GetMember(node.Member.Name).First();
                return Expression.MakeMemberAccess(newObj, newMember);
            }
        }

    }

    public class ExpressionResolutionResult
    {
        public Expression ResolutionExpression { get; private set; }
        public Type Type { get; private set; }

        public ExpressionResolutionResult(Expression resolutionExpression, Type type)
        {
            ResolutionExpression = resolutionExpression;
            Type = type;
        }
    }
}
