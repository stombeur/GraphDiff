using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using RefactorThis.GraphDiff.Internal.Graph;

namespace RefactorThis.GraphDiff.Internal
{
    /// <summary>Db load queries</summary>
    internal interface IQueryLoader
    {
        T LoadEntity<T>(T entity, IEnumerable<string> includeStrings, QueryMode queryMode) where T : class;
        T LoadEntity<T>(Expression<Func<T, bool>> keyPredicate, IEnumerable<string> includeStrings, QueryMode queryMode) where T : class;
    }

    internal class QueryLoader : IQueryLoader
    {
        private readonly DbContext _context;
        private readonly IEntityManager _entityManager;

        public QueryLoader(DbContext context, IEntityManager entityManager)
        {
            _entityManager = entityManager;
            _context = context;
        }

        public T LoadEntity<T>(T entity, IEnumerable<string> includeStrings, QueryMode queryMode) where T : class
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }

            var keyPredicate = CreateKeyPredicateExpression(entity);
            return LoadEntity(keyPredicate, includeStrings, queryMode);
        }

        private IQueryable<T> CreateInclude<T>(IQueryable<T> current, string include) where T : class
        {
            // let's pretend there is only one oftype
            if (include.HasOfType())
            {
                var ofType = OfTypeIncludeString.FromString(include);

                MethodInfo method = typeof(Queryable).GetMethod("OfType");
                MethodInfo generic = method.MakeGenericMethod(new Type[] { ofType.Type });

                IQueryable<T> result = current;
                if (!string.IsNullOrWhiteSpace(ofType.ParentInclude)) result = result.Include(ofType.ParentInclude); //do parent includes first
                var o = generic.Invoke(null, new object[] { result });
                result = ((IQueryable<T>) generic.Invoke(null, new object[] {result})); //then do oftype
                return  result.Include(ofType.Include);
            }
            return current.Include(include);
        }

        public T LoadEntity<T>(Expression<Func<T, bool>> keyPredicate, IEnumerable<string> includeStrings, QueryMode queryMode) where T : class
        {
            if (queryMode == QueryMode.SingleQuery)
            {
                var query = _context.Set<T>().AsQueryable();
                query = includeStrings.Aggregate(query, CreateInclude);
                return query.SingleOrDefault(keyPredicate);
            }

            if (queryMode == QueryMode.MultipleQuery)
            {
                // This is experimental - needs some testing.
                foreach (var include in includeStrings)
                {
                    var query = _context.Set<T>().AsQueryable();
                    query = query.Include(include);
                    query.SingleOrDefault(keyPredicate);
                }

                return _context.Set<T>().Local.AsQueryable().SingleOrDefault(keyPredicate);
            }

            throw new ArgumentOutOfRangeException("queryMode", "Unknown QueryMode");
        }

        private Expression<Func<T, bool>> CreateKeyPredicateExpression<T>(T entity)
        {
            // get key properties of T
            var keyProperties = _entityManager.GetPrimaryKeyFieldsFor(typeof(T)).ToList();

            ParameterExpression parameter = Expression.Parameter(typeof(T));
            Expression expression = CreateEqualsExpression(entity, keyProperties[0], parameter);
            for (int i = 1; i < keyProperties.Count; i++)
            {
                expression = Expression.And(expression, CreateEqualsExpression(entity, keyProperties[i], parameter));
            }

            return Expression.Lambda<Func<T, bool>>(expression, parameter);
        }

        private static Expression CreateEqualsExpression(object entity, PropertyInfo keyProperty, Expression parameter)
        {
            return Expression.Equal(Expression.Property(parameter, keyProperty), Expression.Constant(keyProperty.GetValue(entity, null), keyProperty.PropertyType));
        }
    }
}
