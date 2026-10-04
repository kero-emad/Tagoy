using church.Models;
using church.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.OpenApi.Models;
using System.Reflection;

namespace church
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // =====================================================
            // CORS
            // =====================================================

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("Allowall", policy =>
                {
                    policy
                        .AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                });
            });

            // =====================================================
            // CONTROLLERS
            // =====================================================

            builder.Services.AddControllers();

            // =====================================================
            // DATABASE
            // =====================================================

            builder.Services.AddDbContext<context>(options =>
            {
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("con")
                );
            });

            // =====================================================
            // SWAGGER
            // =====================================================

            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddSwaggerGen(c =>
            {
                /*
                var xmlFile =
                    $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";

                var xmlPath =
                    Path.Combine(
                        AppContext.BaseDirectory,
                        xmlFile
                    );

                c.IncludeXmlComments(xmlPath);
                */

                c.AddSecurityDefinition(
                    "Bearer",
                    new OpenApiSecurityScheme
                    {
                        Name = "Authorization",
                        Type = SecuritySchemeType.ApiKey,
                        Scheme = "Bearer",
                        BearerFormat = "JWT",
                        In = ParameterLocation.Header,
                        Description = "Enter token here"
                    }
                );

                c.AddSecurityRequirement(
                    new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference =
                                    new OpenApiReference
                                    {
                                        Type =
                                            ReferenceType.SecurityScheme,

                                        Id =
                                            "Bearer"
                                    }
                            },

                            Array.Empty<string>()
                        }
                    }
                );
            });

            // =====================================================
            // JWT AUTHENTICATION
            // =====================================================

            builder.Services
                .AddAuthentication(
                    JwtBearerDefaults.AuthenticationScheme
                )
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters =
                        new TokenValidationParameters
                        {
                            ValidateIssuer =
                                true,

                            ValidateAudience =
                                true,

                            ValidateLifetime =
                                true,

                            ValidateIssuerSigningKey =
                                true,

                            ValidIssuer =
                                builder.Configuration[
                                    "Jwt:Issuer"
                                ],

                            ValidAudience =
                                builder.Configuration[
                                    "Jwt:Audience"
                                ],

                            IssuerSigningKey =
                                new SymmetricSecurityKey(
                                    Encoding.UTF8.GetBytes(
                                        builder.Configuration[
                                            "Jwt:Key"
                                        ]!
                                    )
                                )
                        };
                });

            // =====================================================
            // AUTHORIZATION
            // =====================================================

            builder.Services.AddAuthorization();

            // =====================================================
            // HTTP CLIENT
            // Used by AIController and internal API calls
            // =====================================================

            builder.Services.AddHttpClient();

            // =====================================================
            // AI + FIRESTORE SERVICES
            // =====================================================

            builder.Services.AddAiFirestoreServices();

            // =====================================================
            // BUILD APP
            // =====================================================

            var app = builder.Build();

            // =====================================================
            // CORS
            // =====================================================

            app.UseCors("Allowall");

            // =====================================================
            // STATIC FILES
            // =====================================================

            app.UseStaticFiles(
                new StaticFileOptions
                {
                    OnPrepareResponse = ctx =>
                    {
                        ctx.Context.Response.Headers.Append(
                            "Access-Control-Allow-Origin",
                            "*"
                        );

                        ctx.Context.Response.Headers.Append(
                            "Access-Control-Allow-Headers",
                            "*"
                        );

                        ctx.Context.Response.Headers.Append(
                            "Access-Control-Allow-Methods",
                            "*"
                        );
                    }
                }
            );

            /*
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add(
                    "Access-Control-Allow-Origin",
                    "*"
                );

                context.Response.Headers.Add(
                    "Access-Control-Allow-Headers",
                    "*"
                );

                context.Response.Headers.Add(
                    "Access-Control-Allow-Methods",
                    "*"
                );

                if (
                    context.Request.Method ==
                    HttpMethods.Options
                )
                {
                    context.Response.StatusCode =
                        200;

                    await context.Response
                        .CompleteAsync();

                    return;
                }

                await next();
            });
            */

            // =====================================================
            // SWAGGER
            // =====================================================

            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint(
                    "/swagger/v1/swagger.json",
                    "Church API v1"
                );

                c.RoutePrefix =
                    "swagger";
            });

            // =====================================================
            // HTTPS
            // =====================================================

            app.UseHttpsRedirection();

            // =====================================================
            // AUTH
            // =====================================================

            app.UseAuthentication();
            app.UseAuthorization();

            // =====================================================
            // CONTROLLERS
            // =====================================================

            app.MapControllers();

            // app.Urls.Add("http://0.0.0.0:5000");

            app.Run();
        }
    }
}