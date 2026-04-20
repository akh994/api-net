using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkeletonApi.Common.Interfaces;
using SkeletonApi.Common.Services;

namespace SkeletonApi.Common.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonServices(this IServiceCollection services)
    {
        // User Context
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, UserContext>();

        // Propagation
        services.AddTransient<SkeletonApi.Common.RestClient.ClaimsPropagationHandler>();
        services.AddScoped<SkeletonApi.Common.GrpcClient.ClaimsPropagationInterceptor>();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection("Jwt");
        var secretKey = jwtSettings["Secret"];
        var issuer = jwtSettings["Issuer"];
        var audience = jwtSettings["Audience"];

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var tokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrEmpty(secretKey) && !string.IsNullOrEmpty(issuer),
                ValidIssuer = issuer,
                ValidateAudience = !string.IsNullOrEmpty(secretKey) && !string.IsNullOrEmpty(audience),
                ValidAudience = audience,
                ValidateLifetime = !string.IsNullOrEmpty(secretKey),
                ClockSkew = System.TimeSpan.Zero
            };

            if (!string.IsNullOrEmpty(secretKey))
            {
                tokenValidationParameters.ValidateIssuerSigningKey = true;
                tokenValidationParameters.IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secretKey));
            }
            else
            {
                // Allow parsing without validation
                tokenValidationParameters.ValidateIssuerSigningKey = false;
                tokenValidationParameters.RequireSignedTokens = false;
                tokenValidationParameters.SignatureValidator = delegate (string token, Microsoft.IdentityModel.Tokens.TokenValidationParameters parameters)
                {
                    var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(token);
                    return jwt;
                };
            }

            options.TokenValidationParameters = tokenValidationParameters;
        });

        return services;
    }

    public static IServiceCollection AddCommonCors(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var serverConfig = configuration.GetSection("Server").Get<SkeletonApi.Common.Configuration.ServerOptions>()
            ?? new SkeletonApi.Common.Configuration.ServerOptions();
        var corsOptions = serverConfig.Cors;

        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                if (corsOptions.AllowedOrigins.Contains("*"))
                    builder.AllowAnyOrigin();
                else
                    builder.WithOrigins(corsOptions.AllowedOrigins);

                if (corsOptions.AllowedMethods.Contains("*"))
                    builder.AllowAnyMethod();
                else
                    builder.WithMethods(corsOptions.AllowedMethods);

                if (corsOptions.AllowedHeaders.Contains("*"))
                    builder.AllowAnyHeader();
                else
                    builder.WithHeaders(corsOptions.AllowedHeaders);

                if (corsOptions.AllowCredentials && !corsOptions.AllowedOrigins.Contains("*"))
                    builder.AllowCredentials();
            });
        });

        return services;
    }
}
