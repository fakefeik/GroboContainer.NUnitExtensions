using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using GroboContainer.NUnitExtensions.Impl.TopologicalSorting;

using JetBrains.Annotations;

using NUnit.Framework;

namespace GroboContainer.NUnitExtensions.Impl
{
    public static class ReflectionHelpers
    {
        [NotNull]
        public static string GetMethodName([NotNull] this MethodInfo test)
        {
            return $"{test.GetFixtureType().FullName}.{test.Name}";
        }

        [NotNull]
        public static Type GetFixtureType([NotNull] this MethodInfo test)
        {
            var fixtureType = test.ReflectedType;
            if (fixtureType == null)
                throw new InvalidOperationException($"test.ReflectedType is null for: {test.Name}");
            return fixtureType;
        }

        [NotNull]
        public static string GetSuiteName([NotNull] this MethodInfo test)
        {
            return suiteNamesCache.GetOrAdd(test.GetFixtureType(), fixtureType =>
                {
                    var suiteNames = GetAttributesForTestFixture<GroboTestSuiteAttribute>(fixtureType).Select(x => x.SuiteName).ToList();
                    var testFixtureAttribute = GetAttributesForType<GroboTestFixtureAttribute>(fixtureType).SingleOrDefault();
                    if (testFixtureAttribute != null)
                        suiteNames.Add(fixtureType.FullName);
                    if (suiteNames.Count > 1)
                        throw new InvalidOperationException($"There are multiple suite names ({string.Join(", ", suiteNames)}) defined for: {test.GetMethodName()}");
                    var suiteName = suiteNames.SingleOrDefault();
                    if (string.IsNullOrEmpty(suiteName))
                        throw new InvalidOperationException($"Suite name is not defined for: {test.GetMethodName()}");
                    return suiteName;
                });
        }

        [NotNull]
        public static List<GroboTestSuiteWrapperAttribute> GetSuiteWrappers([NotNull] this MethodInfo test)
        {
            return suiteWrappersForTest.GetOrAdd(test, GetWrappers<GroboTestSuiteWrapperAttribute>);
        }

        [NotNull]
        public static List<GroboTestMethodWrapperAttribute> GetMethodWrappers([NotNull] this MethodInfo test)
        {
            return methodWrappersForTest.GetOrAdd(test, GetWrappers<GroboTestMethodWrapperAttribute>);
        }

        [CanBeNull]
        public static MethodInfo FindFixtureSetUpMethod([NotNull] this MethodInfo test)
        {
            return fixtureSetUpMethods.GetOrAdd(GetFixtureType(test), FindSingleMethodMarkedWith<GroboTestFixtureSetUpAttribute>);
        }

        [CanBeNull]
        public static MethodInfo FindSetUpMethod([NotNull] this MethodInfo test)
        {
            return setUpMethods.GetOrAdd(GetFixtureType(test), FindSingleMethodMarkedWith<GroboSetUpAttribute>);
        }

        [CanBeNull]
        public static MethodInfo FindTearDownMethod([NotNull] this MethodInfo test)
        {
            return tearDownMethods.GetOrAdd(GetFixtureType(test), FindSingleMethodMarkedWith<GroboTearDownAttribute>);
        }

        [CanBeNull]
        private static MethodInfo FindSingleMethodMarkedWith<TAttribute>([NotNull] Type fixtureType)
        {
            var methods = fixtureType
                          .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                          .Where(x => x.GetCustomAttributes(typeof(TAttribute), true).Any())
                          .ToList();
            if (methods.Count > 1)
                throw new InvalidOperationException($"There are multiple methods marked with {typeof(TAttribute).Name} attribute in: {fixtureType.FullName}");
            return methods.SingleOrDefault();
        }

        public static void EnsureNunitAttributesAbsence([NotNull] this MethodInfo test)
        {
            var fixtureType = GetFixtureType(test);
            if (nunitAttributesPresence.GetOrAdd(fixtureType, HasMethodMarkedWithNUnitAttribute))
                throw new InvalidOperationException($"Prohibited NUnit attributes ({string.Join(", ", forbiddenNunitMethodAttributes.Select(x => x.Name))}) are used in: {fixtureType.FullName}");
        }

        public static bool HasNunitAttributes([NotNull] this MethodInfo test)
        {
            var fixtureType = GetFixtureType(test);
            return nunitAttributesPresence.GetOrAdd(fixtureType, HasMethodMarkedWithNUnitAttribute);
        }

        [NotNull]
        public static List<FieldInfo> GetFieldsForInjection([NotNull] this Type fixtureType)
        {
            return fieldsForInjection.GetOrAdd(fixtureType, DoGetFieldsForInjection);
        }

        [NotNull]
        private static List<FieldInfo> DoGetFieldsForInjection([CanBeNull] Type fixtureType)
        {
            if (fixtureType == null)
                return new List<FieldInfo>();
            return fixtureType
                   .GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   .Where(x => x.GetCustomAttributes(typeof(InjectedAttribute), false).Any())
                   .Concat(DoGetFieldsForInjection(fixtureType.BaseType))
                   .ToList();
        }

        [NotNull]
        public static List<PropertyInfo> GetPropertiesForInjection([NotNull] this Type fixtureType)
        {
            return propertiesForInjection.GetOrAdd(fixtureType, DoGetPropertiesForInjection);
        }

        [NotNull]
        private static List<PropertyInfo> DoGetPropertiesForInjection([CanBeNull] Type fixtureType)
        {
            if (fixtureType == null)
                return new List<PropertyInfo>();
            return fixtureType
                   .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   .Where(x => x.GetCustomAttributes(typeof(InjectedAttribute), false).Any())
                   .Concat(DoGetPropertiesForInjection(fixtureType.BaseType))
                   .ToList();
        }

        private static bool HasMethodMarkedWithNUnitAttribute([NotNull] Type fixtureType)
        {
            return forbiddenNunitMethodAttributes.Any(a => fixtureType.GetMethods(BindingFlags.Instance | BindingFlags.Public).Any(m => IsMarkedWithAttribute(m, a)));
        }

        private static bool IsMarkedWithAttribute([NotNull] MethodInfo method, [NotNull] Type attribute)
        {
            return method.GetCustomAttributes(attribute, true).Any();
        }

        [NotNull]
        private static List<TWrapper> GetWrappers<TWrapper>([NotNull] MethodInfo test) where TWrapper : GroboTestWrapperAttribute
        {
            var fixtureType = GetFixtureType(test);
            var wrappersForFixture = wrappersForFixtureCache.GetOrAdd(fixtureType, GetAttributesForTestFixture<GroboTestWrapperAttribute>);
            var wrappersForMethod = GetAttributesForMethod<TWrapper>(test);
            var visitedWrappers = new HashSet<GroboTestWrapperAttribute>();
            var nodes = new ConcurrentDictionary<GroboTestWrapperAttribute, DependencyNode<GroboTestWrapperAttribute>>();
            var queue = new Queue<GroboTestWrapperAttribute>(wrappersForMethod.Concat(wrappersForFixture));
            while (queue.Count > 0)
            {
                var wrapper = queue.Dequeue();
                if (!visitedWrappers.Add(wrapper))
                    continue;
                var node = nodes.GetOrAdd(wrapper, Node.Create);
                var wrapperDependencies = wrappersForWrapperCache.GetOrAdd(wrapper.GetType(), x => GetAttributesForType<GroboTestWrapperAttribute>(x).ToList());
                foreach (var wrapperDependency in wrapperDependencies)
                {
                    queue.Enqueue(wrapperDependency);
                    var nodeDependency = nodes.GetOrAdd(wrapperDependency, Node.Create);
                    node.DependsOn(nodeDependency);
                }
            }
            return nodes.Values.OrderTopologically().Select(x => x.Payload).OfType<TWrapper>().ToList();
        }

        [NotNull]
        private static List<TAttribute> GetAttributesForTestFixture<TAttribute>([NotNull] Type fixtureType)
        {
            return GetAllTypesToSearchForAttributes(fixtureType).SelectMany(GetAttributesForType<TAttribute>).ToList();
        }

        [NotNull]
        private static List<Type> GetAllTypesToSearchForAttributes([CanBeNull] Type type)
        {
            if (type == null)
                return new List<Type>();
            return new[] {type}
                   .Union(type.GetInterfaces())
                   .Union(GetAllTypesToSearchForAttributes(type.BaseType))
                   .Distinct()
                   .ToList();
        }

        [NotNull]
        private static List<TAttribute> GetAttributesForMethod<TAttribute>([NotNull] MethodInfo method)
        {
            return method.GetCustomAttributes(typeof(TAttribute), true).Cast<TAttribute>().ToList();
        }

        [NotNull]
        private static IEnumerable<TAttribute> GetAttributesForType<TAttribute>([NotNull] Type type)
        {
            return type.GetCustomAttributes(typeof(TAttribute), true).Cast<TAttribute>();
        }

        private static readonly Type[] forbiddenNunitMethodAttributes =
            {
                typeof(SetUpAttribute),
                typeof(TearDownAttribute),
                typeof(OneTimeSetUpAttribute),
                typeof(OneTimeTearDownAttribute),
            };

        private static readonly ConcurrentDictionary<Type, string> suiteNamesCache = new ConcurrentDictionary<Type, string>();
        private static readonly ConcurrentDictionary<Type, bool> nunitAttributesPresence = new ConcurrentDictionary<Type, bool>();
        private static readonly ConcurrentDictionary<Type, List<FieldInfo>> fieldsForInjection = new ConcurrentDictionary<Type, List<FieldInfo>>();
        private static readonly ConcurrentDictionary<Type, List<PropertyInfo>> propertiesForInjection = new ConcurrentDictionary<Type, List<PropertyInfo>>();
        private static readonly ConcurrentDictionary<Type, MethodInfo> fixtureSetUpMethods = new ConcurrentDictionary<Type, MethodInfo>();
        private static readonly ConcurrentDictionary<Type, MethodInfo> setUpMethods = new ConcurrentDictionary<Type, MethodInfo>();
        private static readonly ConcurrentDictionary<Type, MethodInfo> tearDownMethods = new ConcurrentDictionary<Type, MethodInfo>();
        private static readonly ConcurrentDictionary<Type, List<GroboTestWrapperAttribute>> wrappersForFixtureCache = new ConcurrentDictionary<Type, List<GroboTestWrapperAttribute>>();
        private static readonly ConcurrentDictionary<Type, List<GroboTestWrapperAttribute>> wrappersForWrapperCache = new ConcurrentDictionary<Type, List<GroboTestWrapperAttribute>>();
        private static readonly ConcurrentDictionary<MethodInfo, List<GroboTestSuiteWrapperAttribute>> suiteWrappersForTest = new ConcurrentDictionary<MethodInfo, List<GroboTestSuiteWrapperAttribute>>();
        private static readonly ConcurrentDictionary<MethodInfo, List<GroboTestMethodWrapperAttribute>> methodWrappersForTest = new ConcurrentDictionary<MethodInfo, List<GroboTestMethodWrapperAttribute>>();
    }
}