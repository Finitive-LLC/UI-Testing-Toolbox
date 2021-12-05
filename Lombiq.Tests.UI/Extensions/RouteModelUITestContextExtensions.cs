﻿using Lombiq.Tests.UI.Extensions;
using Lombiq.Tests.UI.Services;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Lombiq.HelpfulLibraries.Libraries.Mvc
{
    public static class RouteModelUITestContextExtensions
    {
        /// <summary>
        /// Navigates to the relative URL generated by <see cref="RouteModel"/> for the <paramref
        /// name="actionExpression"/> in the <typeparamref name="TController"/>.
        /// </summary>
        public static void GoTo<TController>(
            this UITestContext context,
            Expression<Action<TController>> actionExpression,
            params (string Key, object Value)[] additionalArguments) =>
            context.GoToRelativeUrl(RouteModel
                .CreateFromExpression(
                    actionExpression,
                    additionalArguments
                        .Select((key, value) => new KeyValuePair<string, string>(key, value.ToString())))
                .ToString(context.TenantName));
    }
}
