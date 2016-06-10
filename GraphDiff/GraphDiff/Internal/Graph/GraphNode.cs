using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Text.RegularExpressions;

namespace RefactorThis.GraphDiff.Internal.Graph
{
    internal static class StringExtensions
    {
        internal static bool HasOfType(this string input)
        {
            return input.Contains(OfTypeIncludeString.Prefix);
        }

    }
    internal class OfTypeIncludeString
    {
        public const string Prefix = "{OfType";
        public const string Separator = "|";
        public const string Postfix = "}";

        public Type Type { get; set; }
        public string Include { get; set; }

        public string ParentInclude { get; set; }

        public static OfTypeIncludeString Create(PropertyInfo accessor, string parentInclude)
        {
            var result = new OfTypeIncludeString()
            {
                Type = accessor.ReflectedType,
                Include = accessor.Name,
                ParentInclude = parentInclude
            };
            return result;
        }

        public override string ToString()
        {
            var result = $"{Prefix}{Separator}{Type.AssemblyQualifiedName}{Separator}{Include}{Postfix}";
            if (!string.IsNullOrWhiteSpace(ParentInclude)) result = ParentInclude + "." + result;
            return result;
        }

        public static OfTypeIncludeString FromString(string input)
        {
            var regex = new Regex(@"(.*)\{OfType\|(.*)\|(.*)\}(.*)");
            var match = regex.Match(input);
            if (match.Success)
            {
                var parentInclude = match.Groups[1].Value;
                parentInclude= parentInclude.TrimEnd('.');

                var result = new OfTypeIncludeString()
                {
                    ParentInclude = parentInclude,
                    Type = Type.GetType(match.Groups[2].Value),
                    Include = match.Groups[3].Value
                };
                return result;
            }
            return null;


        }
    }

    internal class GraphNode
    {
        protected readonly PropertyInfo Accessor;
        private readonly bool _isOfType;

        protected string IncludeString
        {
            get
            {
                var ownIncludeString = Accessor != null ? Accessor.Name : null;
                if (_isOfType && Accessor != null)
                    return OfTypeIncludeString.Create(Accessor, Parent?.IncludeString).ToString();
                return Parent != null && Parent.IncludeString != null
                        ? Parent.IncludeString + "." + ownIncludeString
                        : ownIncludeString;
            }
        }

        public GraphNode Parent { get; set; }
        public Stack<GraphNode> Members { get; private set; }

        public GraphNode()
        {
            Members = new Stack<GraphNode>();
        }

        protected GraphNode(GraphNode parent, PropertyInfo accessor, bool isOfType = false)
        {
            Accessor = accessor;
            _isOfType = isOfType;
            Members = new Stack<GraphNode>();
            Parent = parent;
        }

        // overridden by different implementations
        public virtual void Update<T>(IChangeTracker changeTracker, IEntityManager entityManager, T persisted, T updating) where T : class
        {
            changeTracker.UpdateItem(updating, persisted, true);

            // Foreach branch perform recursive update
            foreach (var member in Members)
            {
                member.Update(changeTracker, entityManager, persisted, updating);
            }
        }

        public List<string> GetIncludeStrings(IEntityManager entityManager)
        {
            var includeStrings = new List<string>();
            var ownIncludeString = IncludeString;
            if (!string.IsNullOrEmpty(ownIncludeString))
            {
                includeStrings.Add(ownIncludeString);
            }

            includeStrings.AddRange(GetRequiredNavigationPropertyIncludes(entityManager));

            foreach (var member in Members)
            {
                includeStrings.AddRange(member.GetIncludeStrings(entityManager));
            }

            return includeStrings;
        }

        public string GetUniqueKey()
        {
            string key = "";
            if (Parent != null && Parent.Accessor != null)
            {
                key += Parent.Accessor.DeclaringType.FullName + "_" + Parent.Accessor.Name;
            }
            else
            {
                key += "NoParent";
            }
            return key + "_" + Accessor.DeclaringType.FullName + "_" + Accessor.Name;
        }

        protected T GetValue<T>(object instance)
        {
            return (T)Accessor.GetValue(instance, null);
        }

        protected void SetValue(object instance, object value)
        {
            Accessor.SetValue(instance, value, null);
        }

        protected virtual IEnumerable<string> GetRequiredNavigationPropertyIncludes(IEntityManager entityManager)
        {
            return new string[0];
        }

        protected List<string> GetMappedNaviationProperties()
        {
            return Members.Select(m => m.Accessor.Name).ToList();
        }

        protected static IEnumerable<string> GetRequiredNavigationPropertyIncludes(IEntityManager entityManager, Type entityType, string ownIncludeString)
        {
            return entityManager
                .GetRequiredNavigationPropertiesForType(entityType)
                .Select(navigationProperty => ownIncludeString + "." + navigationProperty.Name);
        }

        protected static void ThrowIfCollectionType(PropertyInfo accessor, string mappingType)
        {
            if (IsCollectionType(accessor.PropertyType))
                throw new ArgumentException(string.Format("Collection '{0}' can not be mapped as {1} entity. Please map it as {1} collection.", accessor.Name, mappingType));
        }

        private static bool IsCollectionType(Type propertyType)
        {
            return propertyType.IsArray || propertyType.GetInterface(typeof (IEnumerable<>).FullName) != null;
        }
    }
}
