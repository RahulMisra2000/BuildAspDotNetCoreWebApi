﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Library.API.Services;
using Library.API.Entities;
using Microsoft.EntityFrameworkCore;
using Library.API.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Diagnostics;
using NLog.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Newtonsoft.Json.Serialization;
using System.Linq;
using AspNetCoreRateLimit;

namespace Library.API
{
    public class Startup
    {
        public static IConfiguration Configuration;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(setupAction =>
            {
                // If this is true then if the client's Accept header's value is not supported by the Outputformatters here then,
                // HttpStatus Response Code 406 will be sent back to the client
                setupAction.ReturnHttpNotAcceptable = true;
                
                
                setupAction.OutputFormatters.Add(new XmlDataContractSerializerOutputFormatter());
                // setupAction.InputFormatters.Add(new XmlDataContractSerializerInputFormatter());

                var xmlDataContractSerializerInputFormatter = new XmlDataContractSerializerInputFormatter();
                xmlDataContractSerializerInputFormatter.SupportedMediaTypes.Add("application/vnd.marvin.authorwithdateofdeath.full+xml");
                setupAction.InputFormatters.Add(xmlDataContractSerializerInputFormatter);
                
                var jsonInputFormatter = setupAction.InputFormatters.OfType<JsonInputFormatter>().FirstOrDefault();

                if (jsonInputFormatter != null)
                {
                    jsonInputFormatter.SupportedMediaTypes.Add("application/vnd.marvin.author.full+json");
                    jsonInputFormatter.SupportedMediaTypes.Add("application/vnd.marvin.authorwithdateofdeath.full+json");
                }
                
                // **** We get hold of the FIRST JSON output formatter
                var jsonOutputFormatter = setupAction.OutputFormatters.OfType<JsonOutputFormatter>().FirstOrDefault();

                // and if it found then, we configure it to support custom media type shown below.
                // SO, as a result if the client sends an Http Request with an Accept: application/vnd.marvin.hateoas+json
                // the Output Formatter won't throw an error 406 Not Acceptable .....
                // Then in our controller's Action's parameter we could do 
                // [FromHeader(Name="Accept") string x]
                // and then compare x to "application/vnd.marvin.hateoas+json" or to "application/json" and place different
                // stuff in the Http Response body and Http Response Header depending on the API documentation ....
                if (jsonOutputFormatter != null)
                {
                    jsonOutputFormatter.SupportedMediaTypes.Add("application/vnd.marvin.hateoas+json");
                }

            })
            .AddJsonOptions(options => {
                  options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            });

            // register the DbContext on the container, getting the connection string from
            // appSettings (note: use this during development; in a production environment,
            // it's better to store the connection string in an environment variable)
            var connectionString = Configuration["connectionStrings:libraryDBConnectionString"];
            services.AddDbContext<LibraryContext>(o => o.UseSqlServer(connectionString));

            // register the repository
            services.AddScoped<ILibraryRepository, LibraryRepository>();

            services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();

            services.AddScoped<IUrlHelper>(implementationFactory =>
            {
                var actionContext = implementationFactory.GetService<IActionContextAccessor>()
                .ActionContext;
                return new UrlHelper(actionContext);
            });

            services.AddTransient<IPropertyMappingService, PropertyMappingService>();
            services.AddTransient<ITypeHelperService, TypeHelperService>();

            // ***************** This will add directives to the Cache-Control Http Response Header ******************************
            // There are many options and you will get intellisense when you do expirationModelOptions. or validationModelOptions.
            // ************************************ V IMP *******************************
            // This will ALSO add the eTag, Last-Modified & Expires Http headers in addition to the directives in the 
            // Cache-Control Header
            
            services.AddHttpCacheHeaders( (expirationModelOptions) => {
                                                expirationModelOptions.MaxAge = 600;
                                            }, 
                                          (validationModelOptions) => {
                                                validationModelOptions.AddMustRevalidate = true;
                });
            // *********************************************************************************************************************
            
            
            // *****************  This service allows us to do DATA caching in the server's memory *********************************
            //                    This is for DATA caching ... NOT NOT Http Response Caching ...
            //                    This is used by the IP Rate Limiting Middleware for storing API call counts in memory ....
            services.AddMemoryCache();

            
            
            // *****************  This is configuring the https://github.com/stefanprodan/AspNetCoreRateLimit/wiki/IpRateLimitMiddleware
            //                    MIDDLEWARE which controls how frequently the web apis can be called
            services.Configure<IpRateLimitOptions>((options) =>
            {
                options.GeneralRules = new System.Collections.Generic.List<RateLimitRule>()
                {
                    new RateLimitRule()
                    {
                        Endpoint = "*",
                        Limit = 1000,
                        Period = "5m"
                    },
                    new RateLimitRule()
                    {
                        Endpoint = "*",
                        Limit = 200,
                        Period = "10s"
                    }
                };
            });

            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            // ************************************************************************************************************************
            
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, 
            ILoggerFactory loggerFactory, LibraryContext libraryContext)
        {           
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // *************************** GLOBAL EXCEPTION HANDLER ************************************************
                // Any uncaught Exception in our code will be handled by the UseExceptionHandler middleware
                app.UseExceptionHandler(appBuilder =>
                {
                    appBuilder.Run(async context =>
                    {
                        var ex = context.Features.Get<IExceptionHandlerFeature>();
                        if (ex != null)
                        {
                            // Since ILoggerFactory is already injected, we might as well use it for creating a logger and then using it
                            // BUT, in other places in code we can just inject an ILogger<T> into the ctor and then use it 
                            var logger = loggerFactory.CreateLogger("Global exception logger");
                            logger.LogError(500, ex.Error, ex.Error.Message);
                        }

                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync("An unexpected fault happened. Try again later.");
                    });                      
                });
            }

            AutoMapper.Mapper.Initialize(cfg =>
            {
                cfg.CreateMap<Entities.Author, Models.AuthorDto>()
                    .ForMember(dest => dest.Name, opt => opt.MapFrom(src =>
                    $"{src.FirstName} {src.LastName}"))
                    .ForMember(dest => dest.Age, opt => opt.MapFrom(src =>
                    src.DateOfBirth.GetCurrentAge(src.DateOfDeath)));

                cfg.CreateMap<Entities.Book, Models.BookDto>();

                cfg.CreateMap<Models.AuthorForCreationDto, Entities.Author>();

                cfg.CreateMap<Models.AuthorForCreationWithDateOfDeathDto, Entities.Author>();

                cfg.CreateMap<Models.BookForCreationDto, Entities.Book>();

                cfg.CreateMap<Models.BookForUpdateDto, Entities.Book>();

                cfg.CreateMap<Entities.Book, Models.BookForUpdateDto>();
            });
            
            libraryContext.EnsureSeedDataForContext();

            app.UseIpRateLimiting();

            // ************** This middleware MUST be placed BEFORE the UseMvc so that it can stop the Request from processing 
            // any further and return Http Status code of 304 Not Modified or 412 PreCondition Failed ... if need be
            // Also this middleware adds Cache-Control Response Headers ..... based on how they are configured in the above method.
            app.UseHttpCacheHeaders();

            app.UseMvc(); 
        }
    }
}
