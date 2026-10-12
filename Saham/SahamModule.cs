using Microsoft.AspNetCore.Cors;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Saham.Data;
using Saham.Localization;
using Saham.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Validation.AspNetCore;
using Volo.Abp;
using Volo.Abp.Uow;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.SettingManagement;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Basic;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Basic.Bundling;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.AutoMapper;
using Volo.Abp.Caching;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Emailing;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Swashbuckle;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.Validation.Localization;
using Volo.Abp.VirtualFileSystem;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement.OpenIddict;
using Volo.Abp.Security.Claims;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Aws;
using Saham.Entities.Drivers;
using Saham.Integrations.Storage;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.PostgreSql;
using Volo.Abp.Studio.Client.AspNetCore;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Saham.BackgroundWorkers;
using Saham.Infrastructure.Auditing;
using Saham.Entities.Identity;
using Saham.Entities.Notifications;
using Saham.Entities.Restaurants;
using Saham.Entities.Pricing;
using Saham.Entities.Orders;
using Saham.Entities.Drivers;
using Saham.Entities.Dispatch;
using Saham.Entities.Ledger;
using Saham.Identity;
using Saham.Infrastructure.Idempotency;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.SignalR;
using Volo.Abp.Auditing;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Sms;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.AspNetCore.Uow;
using NetTopologySuite.IO.Converters;
using Volo.Abp.Json.SystemTextJson;
using Volo.Abp.Timing;

namespace Saham;

[DependsOn(
    // ABP Framework packages
    typeof(AbpAspNetCoreMvcModule),
    typeof(AbpAutofacModule),
    typeof(AbpAutoMapperModule),
    typeof(AbpCachingModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpStudioClientAspNetCoreModule),
    typeof(AbpAspNetCoreSignalRModule),
    typeof(AbpSmsModule),
    typeof(AbpBackgroundWorkersModule),

    // theme
    typeof(AbpAspNetCoreMvcUiBasicThemeModule),

    // Account module packages
    typeof(AbpAccountApplicationModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpAccountWebOpenIddictModule),

    // Identity module packages
    typeof(AbpPermissionManagementDomainIdentityModule),
    typeof(AbpPermissionManagementDomainOpenIddictModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpIdentityHttpApiModule),

    // Permission Management module packages
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpPermissionManagementHttpApiModule),

    // Feature Management module packages
    typeof(AbpFeatureManagementHttpApiModule),
    typeof(AbpFeatureManagementApplicationModule),

    // Setting Management module packages
    typeof(AbpSettingManagementHttpApiModule),
    typeof(AbpSettingManagementApplicationModule),

    // Entity Framework Core packages for the used modules
    typeof(AbpAuditLoggingEntityFrameworkCoreModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpFeatureManagementEntityFrameworkCoreModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpSettingManagementEntityFrameworkCoreModule),
    typeof(AbpBackgroundJobsEntityFrameworkCoreModule),
    typeof(BlobStoringDatabaseEntityFrameworkCoreModule),
    typeof(AbpBlobStoringAwsModule),
    typeof(AbpEntityFrameworkCorePostgreSqlModule)
)]
public class SahamModule : AbpModule
{
    /* Single point to enable/disable multi-tenancy */
    public const bool IsMultiTenant = false;

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        context.Services.PreConfigure<AbpMvcDataAnnotationsLocalizationOptions>(options =>
        {
            options.AddAssemblyResource(
                typeof(SahamResource)
            );
        });

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("Saham");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        PreConfigure<OpenIddictServerBuilder>(builder =>
        {
            builder.Configure(options =>
            {
                options.GrantTypes.Add(PhoneOtpTokenExtensionGrant.ExtensionGrantName);
            });
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate("openiddict.pfx", configuration["AuthServer:CertificatePassPhrase"]!);
            });
        }

        SahamGlobalFeatureConfigurator.Configure();
        SahamModuleExtensionConfigurator.Configure();
        SahamEfCoreEntityExtensionMappings.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        if (!hostingEnvironment.IsProduction())
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.LogCompleteSecurityArtifact = true;
        }

        if (hostingEnvironment.IsDevelopment())
        {
            context.Services.Replace(ServiceDescriptor.Singleton<IEmailSender, NullEmailSender>());
        }

        ConfigureAuthentication(context);
        ConfigureBundles();
        ConfigureMultiTenancy();
        ConfigureUrls(configuration);
        ConfigureAutoMapper(context);
        ConfigureHealthChecks(context);
        ConfigureSwagger(context.Services, configuration);
        ConfigureAutoApiControllers();
        ConfigureLocalization();
        ConfigureCors(context, configuration);
        ConfigureDataProtection(context);
        ConfigureVirtualFiles(hostingEnvironment);
        ConfigureEfCore(context);
        ConfigureExtensionGrants();
        ConfigureExceptionHandling();
        ConfigureIdempotency();
        ConfigureRealtime();
        ConfigureEntityHistory();
        ConfigureClock();
        ConfigureJson();
        ConfigureEntityChanges();
        ConfigureNotifications(configuration);
        ConfigureBlobStoring(configuration);
    }

    /* "Notifications" section: test-only switches for the notification queue (NotificationOptions). */
    private void ConfigureNotifications(IConfiguration configuration)
    {
        Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        Configure<Integrations.Wallet.WalletOptions>(configuration.GetSection("Wallet"));
        Configure<Integrations.Calls.CallOptions>(configuration.GetSection("Calls"));
    }

    /* "BlobStorage:S3" section: files go to S3-compatible object storage, the database keeps only
     * their metadata (DriverDocument) or URL (LogoUrl, ImageUrl). One private bucket per container.
     * Without keys (tests, a fresh clone) the database provider stays. */
    private void ConfigureBlobStoring(IConfiguration configuration)
    {
        var s3 = configuration.GetSection("BlobStorage:S3").Get<S3StorageOptions>() ?? new S3StorageOptions();
        Configure<S3StorageOptions>(configuration.GetSection("BlobStorage:S3"));

        if (!s3.IsConfigured)
        {
            return;
        }

        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.Configure<DriverDocumentContainer>(container => UseS3(container, s3, s3.DriverDocumentsBucket));
            options.Containers.Configure<RestaurantMediaContainer>(container => UseS3(container, s3, s3.RestaurantMediaBucket));
        });
    }

    private static void UseS3(BlobContainerConfiguration container, S3StorageOptions s3, string bucket)
    {
        container.UseAws(aws =>
        {
            aws.AccessKeyId = s3.AccessKeyId;
            aws.SecretAccessKey = s3.SecretAccessKey;
            aws.UseCredentials = true;
            aws.Region = s3.Region;
            aws.ContainerName = bucket;
            aws.CreateContainerIfNotExists = s3.CreateBucketIfNotExists;
        });
    }

    /* ABP would otherwise "touch" (ConcurrencyStamp, LastModificationTime) an aggregate whenever a
     * related row is inserted, e.g. every journal entry referenced by a cash movement, or every ledger
     * account receiving a line. That breaks the immutable ledger (the trigger rejects it) and makes
     * concurrent postings conflict on shared accounts. Aggregates here are updated explicitly. */
    private void ConfigureEntityChanges()
    {
        Configure<AbpEntityChangeOptions>(options =>
        {
            options.UpdateAggregateRootWhenNavigationChanges = false;
            options.PublishEntityUpdatedEventWhenNavigationChanges = false;
        });
    }

    /* PostGIS points/polygons are written as GeoJSON wherever ABP serializes entities
     * (entity change history, audit logs); the default serializer fails on NaN Z/M values. */
    private void ConfigureJson()
    {
        Configure<AbpSystemTextJsonSerializerOptions>(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new GeoJsonConverterFactory());
        });
    }


    /* All times are stored and returned in UTC; clients convert to the city's time zone. */
    private void ConfigureClock()
    {
        Configure<AbpClockOptions>(options =>
        {
            options.Kind = DateTimeKind.Utc;
        });
    }

    private void ConfigureExtensionGrants()
    {
        Configure<AbpOpenIddictExtensionGrantsOptions>(options =>
        {
            options.Grants.Add(PhoneOtpTokenExtensionGrant.ExtensionGrantName, new PhoneOtpTokenExtensionGrant());
        });
    }

    /* Business errors that mean "the state changed under you" are 409 (SRS 10.7, EX-11). */
    private void ConfigureExceptionHandling()
    {
        Configure<AbpExceptionHttpStatusCodeOptions>(options =>
        {
            options.Map(SahamErrorCodes.InvalidStateTransition, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.IdempotencyConflict, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.RequestInProgress, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.IdempotencyKeyRequired, HttpStatusCode.BadRequest);
            options.Map(SahamErrorCodes.InvalidPhoneNumber, HttpStatusCode.BadRequest);
            options.Map(SahamErrorCodes.OtpTooManyRequests, HttpStatusCode.TooManyRequests);
            options.Map(SahamErrorCodes.PhoneAlreadyRegistered, HttpStatusCode.Conflict);

            // Invalid input detected by domain rules
            foreach (var code in new[]
                     {
                         SahamErrorCodes.InvalidStaffRole, SahamErrorCodes.InvalidRestaurantStaffRole,
                         SahamErrorCodes.InvalidAmount, SahamErrorCodes.InvalidLocation,
                         SahamErrorCodes.InvalidServiceArea, SahamErrorCodes.RestaurantIdRequired,
                         SahamErrorCodes.InvalidCommissionRate, SahamErrorCodes.InvalidPrepMinutes,
                         SahamErrorCodes.InvalidPauseUntil, SahamErrorCodes.PauseReasonRequired,
                         SahamErrorCodes.InvalidOpeningHours, SahamErrorCodes.MenuCategoryNotInRestaurant,
                         SahamErrorCodes.AddressDescriptionTooShort, SahamErrorCodes.AddressOutsideServiceArea,
                         SahamErrorCodes.InvalidPricingPlan, SahamErrorCodes.InvalidPricingTiers,
                         SahamErrorCodes.EmptyCart, SahamErrorCodes.InvalidQuantity
                     })
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }

            // The cart cannot be priced as it is (SRS 15: 422 on POST /quotes)
            foreach (var code in new[]
                     {
                         SahamErrorCodes.ItemUnavailable, SahamErrorCodes.RestaurantClosed,
                         SahamErrorCodes.BelowMinOrder, SahamErrorCodes.AddressOutOfRange,
                         SahamErrorCodes.InvalidScheduleTime, SahamErrorCodes.NoActivePricingPlan
                     })
            {
                options.Map(code, HttpStatusCode.UnprocessableEntity);
            }

            options.Map(SahamErrorCodes.PricingPlanNotEditable, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.QuoteExpired, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.QuoteAlreadyUsed, HttpStatusCode.Conflict);

            // Orders
            options.Map(SahamErrorCodes.PriceChanged, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.OrderOnHold, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.NoItemDecisionPending, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.CustomerCannotCancel, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.WalletNotAvailable, HttpStatusCode.UnprocessableEntity);
            options.Map(SahamErrorCodes.CashNotAllowed, HttpStatusCode.UnprocessableEntity);
            options.Map(SahamErrorCodes.CashTenderedTooLow, HttpStatusCode.UnprocessableEntity);
            options.Map(SahamErrorCodes.CancelNoteRequired, HttpStatusCode.BadRequest);
            options.Map(SahamErrorCodes.OrderItemNotFound, HttpStatusCode.BadRequest);

            // Drivers & dispatch: "the state changed under you" = 409, invalid input = 400
            foreach (var code in new[]
                     {
                         SahamErrorCodes.OfferExpired, SahamErrorCodes.OfferNotPending, SahamErrorCodes.OrderAlreadyAssigned,
                         SahamErrorCodes.OrderNotDispatchable, SahamErrorCodes.DriverAtCapacity, SahamErrorCodes.DriverHasActiveOrders,
                         SahamErrorCodes.DriverNotAvailable, SahamErrorCodes.DriverCashBlocked, SahamErrorCodes.NoActiveAssignment,
                         SahamErrorCodes.AssignmentAlreadyPickedUp, SahamErrorCodes.DriverAlreadyRegistered,
                         SahamErrorCodes.DocumentAlreadyAccepted, SahamErrorCodes.InvalidDriverReviewState,
                         SahamErrorCodes.DriverDocumentsIncomplete
                     })
            {
                options.Map(code, HttpStatusCode.Conflict);
            }

            options.Map(SahamErrorCodes.AssignmentReleased, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.ContactAttemptRequired, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.InvalidCashAmount, HttpStatusCode.BadRequest);
            options.Map(SahamErrorCodes.ChangeShortfallTooLarge, HttpStatusCode.BadRequest);
            options.Map(SahamErrorCodes.FloatReturnTooLarge, HttpStatusCode.BadRequest);
            options.Map(SahamErrorCodes.DeliveryCodeLocked, HttpStatusCode.Conflict);
            options.Map(SahamErrorCodes.TooFarFromRestaurant, HttpStatusCode.UnprocessableEntity);
            options.Map(SahamErrorCodes.NoShowTooEarly, HttpStatusCode.UnprocessableEntity);

            // Phase 8 / 9: state conflicts = 409, bad input = 400, a coupon or price that does not apply = 422
            foreach (var code in new[]
                     {
                         SahamErrorCodes.IncidentAlreadyResolved, SahamErrorCodes.NotificationNotDead,
                         SahamErrorCodes.RefundExceedsPayment, SahamErrorCodes.InvalidRefundStatus,
                         SahamErrorCodes.CouponAlreadyUsed, SahamErrorCodes.InvalidCouponStatus, SahamErrorCodes.CouponCodeTaken,
                         SahamErrorCodes.NoSubstituteProposed, SahamErrorCodes.LateCancelOnlyAfterAcceptance,
                         SahamErrorCodes.InvalidSettlementStatus, SahamErrorCodes.SettlementPeriodNotClosed,
                         SahamErrorCodes.CustomerAppOffline, SahamErrorCodes.CallNotActive
                     })
            {
                options.Map(code, HttpStatusCode.Conflict);
            }

            foreach (var code in new[]
                     {
                         SahamErrorCodes.IncidentSubjectRequired, SahamErrorCodes.InvalidCouponTerms,
                         SahamErrorCodes.CouponRestaurantRequired, SahamErrorCodes.InvalidSubstitute,
                         SahamErrorCodes.InvalidSettlementPeriod, SahamErrorCodes.UnknownReport
                     })
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }

            foreach (var code in new[]
                     {
                         SahamErrorCodes.CouponNotFound, SahamErrorCodes.CouponNotValidNow, SahamErrorCodes.CouponNotForRestaurant,
                         SahamErrorCodes.CouponBelowMinSubtotal, SahamErrorCodes.CouponUsageLimitReached
                     })
            {
                options.Map(code, HttpStatusCode.UnprocessableEntity);
            }
            options.Map(SahamErrorCodes.NoDriverLocation, HttpStatusCode.UnprocessableEntity);
            options.Map(SahamErrorCodes.DocumentFileRemoved, HttpStatusCode.Gone);

            foreach (var code in new[]
                     {
                         SahamErrorCodes.ReasonRequired, SahamErrorCodes.DocumentInvalidType, SahamErrorCodes.DocumentTooLarge,
                         SahamErrorCodes.ImageInvalidType
                     })
            {
                options.Map(code, HttpStatusCode.BadRequest);
            }
        });
    }

    /* A SignalR connection is one long HTTP request: it must not hold a unit of work for its
     * whole lifetime (the hub opens short ones when it reads the database). */
    private void ConfigureRealtime()
    {
        Configure<AbpAspNetCoreUnitOfWorkOptions>(options =>
        {
            options.IgnoredUrls.Add("/signalr-hubs");
        });
    }

    /* Added after ABP's unit-of-work filter, so it runs inside the request transaction. */
    private void ConfigureIdempotency()
    {
        Configure<MvcOptions>(options =>
        {
            options.Filters.AddService<IdempotencyActionFilter>();
        });
    }

    /* "Who changed what and when" (S-15, SRS 5.5) for every Saham entity,
     * except technical tables that hold secrets or high-volume rows. */
    private void ConfigureEntityHistory()
    {
        var excluded = new[] { typeof(OtpChallenge), typeof(SmsLog), typeof(IdempotencyRecord), typeof(Quote),
            typeof(OrderStatusHistory) /* already an audit trail of its own */,
            typeof(DriverLocation), typeof(DeliveryOffer) /* high volume; offers are their own log */,
            typeof(JournalEntry), typeof(JournalLine), typeof(LedgerAccount) /* the ledger is immutable and is its own audit trail */,
            typeof(OutboxEvent), typeof(Notification) /* message queues, high volume */,
            typeof(Entities.Payments.PaymentEvent) /* raw provider webhooks, kept as received */ };

        /* BR-055 also covers users and roles, permissions and settings, which live in ABP's modules. */
        var platform = new[] { typeof(Volo.Abp.Identity.IdentityUser), typeof(Volo.Abp.Identity.IdentityRole),
            typeof(Volo.Abp.PermissionManagement.PermissionGrant), typeof(Volo.Abp.SettingManagement.Setting) };

        Configure<AbpAuditingOptions>(options =>
        {
            options.EntityHistorySelectors.Add(new NamedTypeSelector(
                "SahamEntities",
                type => type.Namespace != null &&
                        type.Namespace.StartsWith("Saham.Entities") &&
                        !excluded.Contains(type)));
            options.EntityHistorySelectors.Add(new NamedTypeSelector("SahamPlatformEntities", type => platform.Contains(type)));
            options.Contributors.Add(new SahamAuditLogContributor());
        });
    }
    

    private void ConfigureHealthChecks(ServiceConfigurationContext context)
    {
        context.Services.AddSahamHealthChecks();
    }
    
    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    private void ConfigureBundles()
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                BasicThemeBundles.Styles.Global,
                bundle => 
                { 
                    bundle.AddFiles("/global-styles.css"); 
                }
            );

            options.ScriptBundles.Configure(
                BasicThemeBundles.Scripts.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-scripts.js");
                }
            );
        });
    }

    private void ConfigureMultiTenancy()
    {
        Configure<AbpMultiTenancyOptions>(options =>
        {
            options.IsEnabled = IsMultiTenant;
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
            options.RedirectAllowedUrls.AddRange(configuration["App:RedirectAllowedUrls"]?.Split(',') ?? Array.Empty<string>());

            options.Applications["Angular"].RootUrl = configuration["App:ClientUrl"];
            options.Applications["Angular"].Urls[AccountUrlNames.PasswordReset] = "account/reset-password";
        });
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<SahamResource>("en")
                .AddBaseTypes(typeof(AbpValidationResource))
                .AddVirtualJson("/Localization/Saham");

            options.DefaultResourceType = typeof(SahamResource);
            
            options.Languages.Add(new LanguageInfo("en", "en", "English")); 
            options.Languages.Add(new LanguageInfo("ar", "ar", "العربية")); 

        });

        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("Saham", typeof(SahamResource));
        });
    }

    private void ConfigureAutoApiControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(SahamModule).Assembly);
        });
    }

    private void ConfigureSwagger(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAbpSwaggerGenWithOAuth(
            configuration["AuthServer:Authority"]!,
            new Dictionary<string, string>
            {
                {"Saham", "Saham API"}
            },
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Saham API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            });
    }

    private void ConfigureAutoMapper(ServiceConfigurationContext context)
    {
        context.Services.AddAutoMapperObjectMapper<SahamModule>();
        Configure<AbpAutoMapperOptions>(options =>
        {
            /* Uncomment `validate: true` if you want to enable the Configuration Validation feature.
             * See AutoMapper's documentation to learn what it is:
             * https://docs.automapper.org/en/stable/Configuration-validation.html
             */
            options.AddMaps<SahamModule>(/* validate: true */);
        });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(
                        configuration["App:CorsOrigins"]?
                            .Split(",", StringSplitOptions.RemoveEmptyEntries)
                            .Select(o => o.RemovePostFix("/"))
                            .ToArray() ?? Array.Empty<string>()
                    )
                    .WithAbpExposedHeaders()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    private void ConfigureDataProtection(ServiceConfigurationContext context)
    {
        context.Services.AddDataProtection().SetApplicationName("Saham");
    }

    private void ConfigureVirtualFiles(IWebHostEnvironment hostingEnvironment)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<SahamModule>();
            if (hostingEnvironment.IsDevelopment())
            {
                /* Using physical files in development, so we don't need to recompile on changes */
                options.FileSets.ReplaceEmbeddedByPhysical<SahamModule>(hostingEnvironment.ContentRootPath);
            }
        });
    }

    private void ConfigureEfCore(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<SahamDbContext>(options =>
        {
            /* You can remove "includeAllEntities: true" to create
             * default repositories only for aggregate roots
             * Documentation: https://docs.abp.io/en/abp/latest/Entity-Framework-Core#add-default-repositories
             */
            options.AddDefaultRepositories(includeAllEntities: true);

            /* Repository GetAsync/WithDetailsAsync load these children by default. */
            options.Entity<Restaurant>(entity =>
            {
                entity.DefaultWithDetailsFunc = query => query.Include(r => r.Hours);
            });
            options.Entity<PricingPlan>(entity =>
            {
                entity.DefaultWithDetailsFunc = query => query.Include(p => p.Tiers);
            });
            options.Entity<Order>(entity =>
            {
                entity.DefaultWithDetailsFunc = query => query.Include(o => o.Items);
            });
            options.Entity<JournalEntry>(entity =>
            {
                entity.DefaultWithDetailsFunc = query => query.Include(e => e.Lines);
            });
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(configurationContext =>
            {
                configurationContext.UseNpgsql(npgsql => npgsql.UseNetTopologySuite());
            });
        });
        
    }


    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseErrorPage();
        }

        app.UseCorrelationId();
        app.UseRouting();
        app.UseStaticFiles();
        app.UseAbpStudioLink();
        app.UseAbpSecurityHeaders();
        app.UseCors();
        // SignalR over WebSockets cannot send headers: accept ?access_token= on hub URLs only.
        app.Use(async (httpContext, next) =>
        {
            var request = httpContext.Request;
            if (request.Path.StartsWithSegments("/signalr-hubs") &&
                !request.Headers.ContainsKey("Authorization") &&
                request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = "Bearer " + token;
            }

            await next();
        });
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();

        if (IsMultiTenant)
        {
            app.UseMultiTenancy();
        }

        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Saham API");
            options.OAuthClientId(context.GetConfiguration()["AuthServer:SwaggerClientId"]);
        });

        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }

    public override async Task OnPostApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        await context.AddBackgroundWorkerAsync<HousekeepingWorker>();
        await context.AddBackgroundWorkerAsync<OrderTimeoutsWorker>();
        await context.AddBackgroundWorkerAsync<DispatchWorker>();
        await context.AddBackgroundWorkerAsync<DriverStaleWorker>();
        await context.AddBackgroundWorkerAsync<CashOverdueWorker>();
        await context.AddBackgroundWorkerAsync<NotificationDispatchWorker>();
        await context.AddBackgroundWorkerAsync<ScheduledReleaseWorker>();
        await context.AddBackgroundWorkerAsync<PaymentTimeoutWorker>();
        await context.AddBackgroundWorkerAsync<RefundWorker>();
        await context.AddBackgroundWorkerAsync<DailyCloseWorker>();
        await context.AddBackgroundWorkerAsync<CallTimeoutWorker>();
    }
}
