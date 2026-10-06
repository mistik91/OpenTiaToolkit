using System;
using System.Reflection;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Core.Services
{
    public class SessionSerializedServiceProxy<TService> : DispatchProxy
        where TService : class
    {
        private TService? _target;
        private EngineeringSessionContext? _sessionContext;

        internal static TService Create(TService target, EngineeringSessionContext sessionContext)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (sessionContext == null)
            {
                throw new ArgumentNullException(nameof(sessionContext));
            }

            var proxy = Create<TService, SessionSerializedServiceProxy<TService>>();
            var typedProxy = (SessionSerializedServiceProxy<TService>)(object)proxy;
            typedProxy._target = target;
            typedProxy._sessionContext = sessionContext;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null)
            {
                throw new ArgumentNullException(nameof(targetMethod));
            }

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
            {
                return InvokeTaskAsync(targetMethod, args);
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var method = typeof(SessionSerializedServiceProxy<TService>)
                    .GetMethod(nameof(InvokeTaskResultAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(resultType);
                return method.Invoke(this, new object?[] { targetMethod, args });
            }

            return SessionContext.RunExclusive(() => InvokeTarget(targetMethod, args));
        }

        private async Task InvokeTaskAsync(MethodInfo targetMethod, object?[]? args)
        {
            await SessionContext.RunExclusiveAsync(async () =>
            {
                var task = (Task)InvokeTarget(targetMethod, args)!;
                await task.ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private async Task<TResult> InvokeTaskResultAsync<TResult>(MethodInfo targetMethod, object?[]? args)
        {
            return await SessionContext.RunExclusiveAsync(async () =>
            {
                var task = (Task<TResult>)InvokeTarget(targetMethod, args)!;
                return await task.ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private object? InvokeTarget(MethodInfo targetMethod, object?[]? args)
        {
            try
            {
                return targetMethod.Invoke(Target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private TService Target => _target ?? throw new InvalidOperationException("Serialized service proxy target was not initialized.");

        private EngineeringSessionContext SessionContext => _sessionContext ?? throw new InvalidOperationException("Serialized service proxy context was not initialized.");
    }
}
