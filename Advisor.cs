using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace SemVerAdvisor
{
    public class Advisor
    {
        public VersionChange CompareAssemblies(Assembly older, Assembly newer)
        {
            bool atLeastMinor = false;

            var olderPublictypes = older.GetExportedTypes();
            var newerPublicTypes = newer.GetExportedTypes();
            var removed = olderPublictypes.Except(newerPublicTypes, typeNameComparer).ToArray();
            if (removed.Length > 0) return VersionChange.Major;

            var added = newerPublicTypes.Except(olderPublictypes, typeNameComparer).ToArray();
            if (added.Length > 0) atLeastMinor = true;

            foreach (var olderType in olderPublictypes)
            {
                var newerType = newer.GetType(olderType.FullName);
                var diff = CompareTypes(olderType, newerType);
                if (diff == VersionChange.Major) return VersionChange.Major;
                if (diff == VersionChange.Minor) atLeastMinor = true;
            }

            if (atLeastMinor) return VersionChange.Minor;

            return VersionChange.Patch;
        }

        public void DoSomething(){}

        class TypeNameEqualityComparer : IEqualityComparer<Type>
        {
            #region IEqualityComparer implementation
            public bool Equals(Type x, Type y)
            {
                return x.FullName.Equals(y.FullName);
            }
            public int GetHashCode(Type obj)
            {
                return obj.Name.GetHashCode();
            }
            #endregion
        }
        TypeNameEqualityComparer typeNameComparer = new TypeNameEqualityComparer();

        VersionChange CompareTypes(Type older, Type newer)
        {
            bool atLeastMinor = false;

            var olderMethods = older.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.InvokeMethod |
                    BindingFlags.Static)
                .Where(mi => mi.IsFamily || mi.IsPublic)
                .ToArray();

            var newerMethods = newer.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.InvokeMethod |
                    BindingFlags.Static)
                .Where(mi => mi.IsFamily || mi.IsPublic)
                .ToArray();

            // compare by method signature
            // TODO: removal is not necessarily breaking. for example, a method
            //  might be removed, but another added exactly the same except for
            //  an additional parameter on the end with a default value.
            //  Although, that is a C#-specific case. Maybe consider just from
            //  the perspective of the CLR and IL.
            var removed = olderMethods.Except(newerMethods, methodNameComparer).ToArray();
            if (removed.Length > 0) return VersionChange.Major;

            var added = newerMethods.Except(olderMethods, methodNameComparer).ToArray();
            if (added.Length > 0) atLeastMinor = true;

            foreach (var olderMethod in olderMethods)
            {
                // TODO: parameter types may be internal to the assembly. in
                //  that case, we'll have to match them up by name.
                var newerMethod =
                    newer.GetMethod(
                        olderMethod.Name,
                        BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Instance |
                            BindingFlags.InvokeMethod |
                            BindingFlags.Static,
                        null,
                        olderMethod.GetParameters()
                            .Select(pi => pi.ParameterType).ToArray(),
                        null);
                var diff = CompareMethods(olderMethod, newerMethod);
                if (diff == VersionChange.Major) return VersionChange.Major;
                if (diff == VersionChange.Minor) atLeastMinor = true;
            }

            if (atLeastMinor) return VersionChange.Minor;

            return VersionChange.Patch;
        }

        class MethodNameEqualityComparer : IEqualityComparer<MethodInfo>
        {
            #region IEqualityComparer implementation
            public bool Equals(MethodInfo x, MethodInfo y)
            {
                return (x.Name.Equals(y.Name));
            }
            public int GetHashCode(MethodInfo obj)
            {
                return obj.Name.GetHashCode();
            }
            #endregion
        }
        MethodNameEqualityComparer methodNameComparer = new MethodNameEqualityComparer();

        VersionChange CompareMethods(MethodInfo older, MethodInfo newer)
        {
            if (!typeNameComparer.Equals(older.ReturnType, newer.ReturnType))
            {
                // TODO: if newer.ReturnType inherits from older.ReturnType (i.e. is more derived), that's backwards-compatible
                // TODO: if older.ReturnType inherits from newer.ReturnType (i.e. newer is less derived), that's a breaking change
                // for now, we'll just consider it breaking
                return VersionChange.Major;
            }

            int i;
            for (i = 0; i < older.GetParameters().Length; i++)
            {
                var olderParam = older.GetParameters()[i];
                var newerParam = newer.GetParameters()[i];

                if (!typeNameComparer.Equals(olderParam.ParameterType, newerParam.ParameterType))
                {
                    // if newerParam.ParameterType is less derived than olderParam.ParameterType, that's backwards compatible
                    // if newerParam.ParameterType is more derived than olderParam.ParameterType, that's a breaking change
                    // for now, we'll consider it breaking in all cases of difference
                    return VersionChange.Major;
                }

                if (olderParam.IsIn != newerParam.IsIn) return VersionChange.Major;
                if (olderParam.IsOut != newerParam.IsOut) return VersionChange.Major;
            }

            return VersionChange.Patch;
        }
    }
}

