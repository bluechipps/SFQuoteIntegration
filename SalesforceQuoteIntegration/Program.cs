using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Sinks.MSSqlServer;
using System.Collections.ObjectModel;
using System.Data;
using SalesforceQuoteIntegration;
using SalesforceQuoteIntegration.Api;
using SalesforceQuoteIntegration.Data;
using SalesforceQuoteIntegration.Services;
using SalesforceQuoteIntegration.Startup;
using SalesforceQuoteIntegration.Models;
using AutoMapper;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .WriteTo.Console()
    .WriteTo.MSSqlServer(
        connectionString: configuration.GetConnectionString("DefaultConnection"),
        sinkOptions: new MSSqlServerSinkOptions
        {
            TableName          = "ApplicationLog",
            AutoCreateSqlTable = true
        }
        //columnOptions: new ColumnOptions
        //{
        //    AdditionalColumns = new Collection<SqlColumn>
        //    {
        //        new SqlColumn { ColumnName = "Properties", DataType = SqlDbType.NVarChar, DataLength = -1 }
        //    }
        //}
    )
    .CreateLogger();

try
{
    Log.Information($"Starting Salesforce Quote Integration Service");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    var connStr = configuration.GetConnectionString("DefaultConnection");

    // Required for IIS reverse proxy — forwards client IP and protocol headers
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Clear known networks/proxies to trust all proxies (safe when behind IIS on same machine)
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlServer(connStr));

    builder.Services.AddSingleton<SalesforceAuthService>(_ =>
        new SalesforceAuthService(
            clientId:     configuration["Salesforce:ClientId"]     ?? throw new Exception("Salesforce:ClientId is required"),
            clientSecret: configuration["Salesforce:ClientSecret"] ?? throw new Exception("Salesforce:ClientSecret is required"),
            loginUrl:     configuration["Salesforce:LoginUrl"]     ?? "https://data-java-8131--muledev.sandbox.my.salesforce.com"
        ));

    builder.Services.AddHttpClient("warmup").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
    builder.Services.AddHostedService<WarmupHostedService>();

    builder.Services.AddSingleton<QuoteStorageService>();
    builder.Services.AddSingleton<RawSqlService>();
    builder.Services.AddSingleton<SalesforceQueryService>();
    builder.Services.AddSingleton<QuoteChangeEventService>();
    builder.Services.AddHostedService<QuoteIntegrationWorker>();
    builder.Services.AddHostedService<ProcessingNotificationsWorker>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title       = "Salesforce Quote Integration API",
            Version     = "v1",
            Description = "Query Salesforce Quote change events and application logs stored from the CDC streaming integration."
        });
    });

    builder.Services.AddAutoMapper(cfg =>
    {
        cfg.CreateMap<sfProduct2, sfProduct2custom>();
        // Add other mappings here
    });

    var app = builder.Build();

    // Must be first in the middleware pipeline so subsequent middleware
    // sees the correct scheme/host forwarded by IIS
    app.UseForwardedHeaders();

    // Apply EF migrations on startup
    //using (var scope = app.Services.CreateScope())
    //{
    //    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    //    await db.Database.MigrateAsync();
    //    Log.Information($"Database migrations applied successfully");
    //}

    // Apply stored procedure scripts on startup
    Dictionary<string, string> spdefs = new()
    {
        { "sp_ebs_rental_pricing", @$"
CREATE OR ALTER PROCEDURE sp_ebs_rental_pricing (
	@kcustnum VARCHAR(8) = '',
	@custsnum VARCHAR(3) = '000',
	@kbranch VARCHAR(3) = '',
	@equipid VARCHAR(20) = ''
)
AS
BEGIN
	SET NOCOUNT ON

	IF (@kcustnum = '' or @kbranch = '' or @equipid = '')
		RAISERROR (N'Missing required sp_ebs_rental_pricing parameters', 16, 1)
	ELSE
	BEGIN
		DECLARE @hourly real, @daily real, @weekly real, @monthly real

		DECLARE @rcl varchar(3), @grp varchar(20), @prd varchar(20), @model varchar(20), @eqloc varchar(3), @eqstatus varchar(2),
				@eqhourly real, @eqdaily real, @eqweekly real, @eqmonthly real
		SET @rcl = (select top 1 custrcl from custmast where kcustnum = @kcustnum and custsnum = @custsnum)
		SELECT TOP 1 @grp = eqpigrp, @prd = gmmottype, @model = kmodel, @eqloc = eqpphybr, @eqstatus = eqpstatus,
			@eqhourly = eqprnthrly, @eqdaily = eqprntday, @eqweekly = eqprntweek, @eqmonthly = eqprntmnth
		FROM equip WHERE kequipnum = @equipid

		IF OBJECT_ID('tempdb..uprrent','U') > 0
			DROP TABLE #uprrent;
		CREATE TABLE #uprrent ([kdeleteflg] varchar(1), [date1] datetime, [kbranch] varchar(3), [custrcl] varchar(2), [eqpigrp] varchar(3), [kmodel] varchar(12), [udesc] varchar(40), [uprcpbase] varchar(1), [uprcpbas01] varchar(1), [uprcppct] real, [uamount] float, [uamount01] float, [uamount02] float, [uamount03] float, [uamount04] float, [uamount05] float, [uamount06] float, [eqpraexcnt] smallint, [eqpraexc01] smallint, [eqpraexc02] smallint, [eqpraexc03] smallint, [eqpraexc04] smallint, [eqpraexc05] smallint, [usummary] varchar(1), [utaxexcpt] varchar(1), [oetrancode] varchar(2), [glco1] varchar(3), [glacct1] varchar(6), [glbr1] varchar(3), [gldpt1] varchar(3), [glco2] varchar(3), [glacct2] varchar(6), [glbr2] varchar(3), [gldpt2] varchar(3), [glco3] varchar(3), [glacct3] varchar(6), [glbr3] varchar(3), [gldpt3] varchar(3), [glco4] varchar(3), [glacct4] varchar(6), [glbr4] varchar(3), [gldpt4] varchar(3), [glco5] varchar(3), [glacct5] varchar(6), [glbr5] varchar(3), [gldpt5] varchar(3), [glco6] varchar(3), [glacct6] varchar(6), [glbr6] varchar(3), [gldpt6] varchar(3), [uprrprod] varchar(5), [uprrdesc2] varchar(40), [uprrdesc3] varchar(40), [uprrconpro] varchar(1), [uprrfrdte] datetime, [uprrtodte] datetime, [uprrcondes] varchar(40), [uprroper] real, [uprraddday] real, [uprraddwk] real, [uprraddmo] real, [uprrlngtrm] float, [uprrtravel] real, [uprrunaday] smallint, [uprrunawk] smallint, [uprrunamo] smallint, [uprpromrt] real, [uprpromr01] real, [uprpromr02] real, [uprpromr03] real, [uprpromr04] real, [uprpromr05] real, [uprpromr06] real, [uprpromr07] real, [uprpromr08] real, [uprpromr09] real, [uprpromhrs] smallint, [uprpromh01] smallint, [uprpromh02] smallint, [uprpromh03] smallint, [uprpromh04] smallint, [uprpromh05] smallint, [uprpromot] real, [uprpromo01] real, [uprpromo02] real, [uprprobase] smallint, [uprproba01] smallint, [myorder] varchar(1))

		INSERT INTO #uprrent
			SELECT [kdeleteflg], [date1], [kbranch], [custrcl], [eqpigrp], [kmodel], [udesc], [uprcpbase], [uprcpbas01], [uprcppct], [uamount], [uamount01], [uamount02], [uamount03], [uamount04], [uamount05], [uamount06], [eqpraexcnt], [eqpraexc01], [eqpraexc02], [eqpraexc03], [eqpraexc04], [eqpraexc05], [usummary], [utaxexcpt], [oetrancode], [glco1], [glacct1], [glbr1], [gldpt1], [glco2], [glacct2], [glbr2], [gldpt2], [glco3], [glacct3], [glbr3], [gldpt3], [glco4], [glacct4], [glbr4], [gldpt4], [glco5], [glacct5], [glbr5], [gldpt5], [glco6], [glacct6], [glbr6], [gldpt6], [uprrprod], [uprrdesc2], [uprrdesc3], [uprrconpro], [uprrfrdte], [uprrtodte], [uprrcondes], [uprroper], [uprraddday], [uprraddwk], [uprraddmo], [uprrlngtrm], [uprrtravel], [uprrunaday], [uprrunawk], [uprrunamo], [uprpromrt], [uprpromr01], [uprpromr02], [uprpromr03], [uprpromr04], [uprpromr05], [uprpromr06], [uprpromr07], [uprpromr08], [uprpromr09], [uprpromhrs], [uprpromh01], [uprpromh02], [uprpromh03], [uprpromh04], [uprpromh05], [uprpromot], [uprpromo01], [uprpromo02], [uprprobase], [uprproba01],
				CASE
					WHEN custrcl = '**' THEN '6'
					WHEN kbranch = '***' THEN '5'
					WHEN eqpigrp = '***' THEN '4'
					WHEN uprrprod = '*****' THEN '3'
					WHEN kmodel = '************' THEN '2'
					ELSE '1' END
				AS myorder
			FROM uprrent
			WHERE  (kbranch = @kbranch AND custrcl = @rcl AND eqpigrp = @grp AND uprrprod = @prd AND kmodel = @model )
				OR (kbranch = @kbranch AND custrcl = @rcl AND eqpigrp = @grp AND uprrprod = @prd AND kmodel = '************' )
				OR (kbranch = @kbranch AND custrcl = @rcl AND eqpigrp = @grp AND uprrprod = '*****' AND kmodel = '************' )
				OR (kbranch = @kbranch AND custrcl = @rcl AND eqpigrp = '***' AND uprrprod = '*****' AND kmodel = '************' )
				OR (kbranch = '***' AND custrcl = @rcl AND eqpigrp = '***' AND uprrprod = '*****' AND kmodel = '************' )
				OR (kbranch = '***' AND custrcl = '**' AND eqpigrp = '***' AND uprrprod = '*****' AND kmodel = '************' )

		IF OBJECT_ID('tempdb..uprrentsp','U') > 0
			DROP TABLE #uprrentsp;
		CREATE TABLE #uprrentsp ([kdeleteflg] varchar(1), [date1] datetime, [kbranch] varchar(3), [kcustnum] varchar(8), [eqpigrp] varchar(3), [kmodel] varchar(12), [udesc] varchar(40), [uprcpbase] varchar(1), [uprcpbas01] varchar(1), [uprcppct] real, [uamount] float, [uamount01] float, [uamount02] float, [uamount03] float, [uamount04] float, [uamount05] float, [uamount06] float, [eqpraexcnt] smallint, [eqpraexc01] smallint, [eqpraexc02] smallint, [eqpraexc03] smallint, [eqpraexc04] smallint, [eqpraexc05] smallint, [usummary] varchar(1), [utaxexcpt] varchar(1), [oetrancode] varchar(2), [glco1] varchar(3), [glacct1] varchar(6), [glbr1] varchar(3), [gldpt1] varchar(3), [glco2] varchar(3), [glacct2] varchar(6), [glbr2] varchar(3), [gldpt2] varchar(3), [glco3] varchar(3), [glacct3] varchar(6), [glbr3] varchar(3), [gldpt3] varchar(3), [glco4] varchar(3), [glacct4] varchar(6), [glbr4] varchar(3), [gldpt4] varchar(3), [glco5] varchar(3), [glacct5] varchar(6), [glbr5] varchar(3), [gldpt5] varchar(3), [glco6] varchar(3), [glacct6] varchar(6), [glbr6] varchar(3), [gldpt6] varchar(3), [uprrprod] varchar(5), [uprrdesc2] varchar(40), [uprrdesc3] varchar(40), [uprrconpro] varchar(1), [uprrfrdte] datetime, [uprrtodte] datetime, [uprrcondes] varchar(40), [uprroper] real, [uprraddday] real, [uprraddwk] real, [uprraddmo] real, [uprrlngtrm] float, [uprrtravel] real, [uprrunaday] smallint, [uprrunawk] smallint, [uprrunamo] smallint, [uprpromrt] real, [uprpromr01] real, [uprpromr02] real, [uprpromr03] real, [uprpromr04] real, [uprpromr05] real, [uprpromr06] real, [uprpromr07] real, [uprpromr08] real, [uprpromr09] real, [uprpromhrs] smallint, [uprpromh01] smallint, [uprpromh02] smallint, [uprpromh03] smallint, [uprpromh04] smallint, [uprpromh05] smallint, [uprpromot] real, [uprpromo01] real, [uprpromo02] real, [uprprobase] smallint, [uprproba01] smallint, [myorder] varchar(1))
		INSERT INTO #uprrentsp
			SELECT [kdeleteflg], [date1], [kbranch], [kcustnum], [eqpigrp], [kmodel], [udesc], [uprcpbase], [uprcpbas01], [uprcppct], [uamount], [uamount01], [uamount02], [uamount03], [uamount04], [uamount05], [uamount06], [eqpraexcnt], [eqpraexc01], [eqpraexc02], [eqpraexc03], [eqpraexc04], [eqpraexc05], [usummary], [utaxexcpt], [oetrancode], [glco1], [glacct1], [glbr1], [gldpt1], [glco2], [glacct2], [glbr2], [gldpt2], [glco3], [glacct3], [glbr3], [gldpt3], [glco4], [glacct4], [glbr4], [gldpt4], [glco5], [glacct5], [glbr5], [gldpt5], [glco6], [glacct6], [glbr6], [gldpt6], [uprrprod], [uprrdesc2], [uprrdesc3], [uprrconpro], [uprrfrdte], [uprrtodte], [uprrcondes], [uprroper], [uprraddday], [uprraddwk], [uprraddmo], [uprrlngtrm], [uprrtravel], [uprrunaday], [uprrunawk], [uprrunamo], [uprpromrt], [uprpromr01], [uprpromr02], [uprpromr03], [uprpromr04], [uprpromr05], [uprpromr06], [uprpromr07], [uprpromr08], [uprpromr09], [uprpromhrs], [uprpromh01], [uprpromh02], [uprpromh03], [uprpromh04], [uprpromh05], [uprpromot], [uprpromo01], [uprpromo02], [uprprobase], [uprproba01],
					CASE
						WHEN kcustnum = '********' THEN '6'
						WHEN kbranch = '***' THEN '5'
						WHEN eqpigrp = '***' THEN '4'
						WHEN uprrprod = '*****' THEN '3'
						WHEN kmodel = '************' THEN '2'
						ELSE '1' END
					AS myorder
			FROM uprrentsp
			WHERE ([kbranch] = @kbranch AND [kcustnum] = @kcustnum AND [eqpigrp] = @grp AND [uprrprod] = @prd AND [kmodel] = @model)
				OR ( [kbranch] = @kbranch AND [kcustnum] = @kcustnum AND [eqpigrp] = @grp AND [uprrprod] = @prd      AND [kmodel] = '************'     )
				OR ( [kbranch] = @kbranch AND [kcustnum] = @kcustnum AND [eqpigrp] = @grp AND [uprrprod] =   '*****' AND [kmodel] = '************'  )
				OR ( [kbranch] = @kbranch AND [kcustnum] = @kcustnum AND [eqpigrp] =  '***' AND [uprrprod] = '*****' AND [kmodel] = '************' )
				OR ( [kbranch] = '***'    AND [kcustnum] = @kcustnum AND [eqpigrp] =  '***' AND [uprrprod] = '*****' AND [kmodel] = '************'    )
				OR ( [kbranch] = @kbranch AND [kcustnum] = '********' AND [eqpigrp] = '***' AND [uprrprod] = '*****' AND [kmodel] = '************' )
				OR ( [kbranch] = '***'    AND [kcustnum] = '********' AND [eqpigrp] = '***' AND [uprrprod] = '*****' AND [kmodel] = '************'   )



		IF EXISTS(SELECT * from #uprrent)
		BEGIN
			DECLARE @pbasetemp varchar(1), @percent real
			SELECT TOP 1 @pbasetemp=uprcpbase, @percent=ISNULL(uprcppct,1.0) 
			FROM #uprrent ORDER BY myorder
				
			IF (@pbasetemp = '1')
			BEGIN
				SELECT TOP 1
					@hourly = uamount03,
					@daily = uamount,
					@weekly = uamount01,
					@monthly = uamount02
				FROM #uprrent
			END
			ELSE IF (@pbasetemp = '2')
			BEGIN
				SELECT TOP 1
					@hourly = @percent * @eqhourly,
					@daily = @percent * @eqdaily,
					@weekly = @percent * @eqweekly,
					@monthly = @percent * @eqmonthly
				FROM #uprrent
			END

			IF EXISTS(SELECT * from #uprrentsp)
			BEGIN
				DECLARE @pbasetemp2 varchar(1), @percent2 real
				SELECT TOP 1 @pbasetemp2=uprcpbase, @percent2=ISNULL(uprcppct,1.0) 
				FROM #uprrentsp ORDER BY myorder
				IF (@pbasetemp2 = 1)
				BEGIN
					SELECT TOP 1
						@hourly = uamount03,
						@daily = uamount,
						@weekly = uamount01,
						@monthly = uamount02
					FROM #uprrentsp
				END
				ELSE IF (@pbasetemp2 = 2)
				BEGIN
					SELECT TOP 1
						-- not sure about this
						@hourly = @percent2 * @hourly,
						@daily = @percent2 * @daily,
						@weekly = @percent2 * @weekly,
						@monthly = @percent2 * @monthly
					FROM #uprrentsp
				END
			END
		END

		IF OBJECT_ID('tempdb..uprrentmaster','U') > 0
			DROP TABLE #uprrentmaster;
		CREATE TABLE #uprrentmaster ([kdeleteflg] varchar(1), [date1] datetime, [kbranch] varchar(3), [eqpigrp] varchar(3), [kmodel] varchar(12), [udesc] varchar(40), [uprcpbase] varchar(1), [uprcpbas01] varchar(1), [uprcppct] real, [uamount] float, [uamount01] float, [uamount02] float, [uamount03] float, [uamount04] float, [uamount05] float, [uamount06] float, [eqpraexcnt] smallint, [eqpraexc01] smallint, [eqpraexc02] smallint, [eqpraexc03] smallint, [eqpraexc04] smallint, [eqpraexc05] smallint, [usummary] varchar(1), [utaxexcpt] varchar(1), [oetrancode] varchar(2), [glco1] varchar(3), [glacct1] varchar(6), [glbr1] varchar(3), [gldpt1] varchar(3), [glco2] varchar(3), [glacct2] varchar(6), [glbr2] varchar(3), [gldpt2] varchar(3), [glco3] varchar(3), [glacct3] varchar(6), [glbr3] varchar(3), [gldpt3] varchar(3), [glco4] varchar(3), [glacct4] varchar(6), [glbr4] varchar(3), [gldpt4] varchar(3), [glco5] varchar(3), [glacct5] varchar(6), [glbr5] varchar(3), [gldpt5] varchar(3), [glco6] varchar(3), [glacct6] varchar(6), [glbr6] varchar(3), [gldpt6] varchar(3), [uprrprod] varchar(5), [uprrdesc2] varchar(40), [uprrdesc3] varchar(40), [uprrconpro] varchar(1), [uprrfrdte] datetime, [uprrtodte] datetime, [uprrcondes] varchar(40), [uprroper] real, [uprraddday] real, [uprraddwk] real, [uprraddmo] real, [uprrlngtrm] float, [uprrtravel] real, [uprrunaday] smallint, [uprrunawk] smallint, [uprrunamo] smallint, [uprpromrt] real, [uprpromr01] real, [uprpromr02] real, [uprpromr03] real, [uprpromr04] real, [uprpromr05] real, [uprpromr06] real, [uprpromr07] real, [uprpromr08] real, [uprpromr09] real, [uprpromhrs] smallint, [uprpromh01] smallint, [uprpromh02] smallint, [uprpromh03] smallint, [uprpromh04] smallint, [uprpromh05] smallint, [uprpromot] real, [uprpromo01] real, [uprpromo02] real, [uprprobase] smallint, [uprproba01] smallint)

		DECLARE @pbase varchar(1)
		;WITH qry as (
			select top 1 [kdeleteflg], [date1], [kbranch], [eqpigrp], [kmodel], [udesc], [uprcpbase], [uprcpbas01], [uprcppct], [uamount], [uamount01], [uamount02], [uamount03], [uamount04], [uamount05], [uamount06], [eqpraexcnt], [eqpraexc01], [eqpraexc02], [eqpraexc03], [eqpraexc04], [eqpraexc05], [usummary], [utaxexcpt], [oetrancode], [glco1], [glacct1], [glbr1], [gldpt1], [glco2], [glacct2], [glbr2], [gldpt2], [glco3], [glacct3], [glbr3], [gldpt3], [glco4], [glacct4], [glbr4], [gldpt4], [glco5], [glacct5], [glbr5], [gldpt5], [glco6], [glacct6], [glbr6], [gldpt6], [uprrprod], [uprrdesc2], [uprrdesc3], [uprrconpro], [uprrfrdte], [uprrtodte], [uprrcondes], [uprroper], [uprraddday], [uprraddwk], [uprraddmo], [uprrlngtrm], [uprrtravel], [uprrunaday], [uprrunawk], [uprrunamo], [uprpromrt], [uprpromr01], [uprpromr02], [uprpromr03], [uprpromr04], [uprpromr05], [uprpromr06], [uprpromr07], [uprpromr08], [uprpromr09], [uprpromhrs], [uprpromh01], [uprpromh02], [uprpromh03], [uprpromh04], [uprpromh05], [uprpromot], [uprpromo01], [uprpromo02], [uprprobase], [uprproba01] from #uprrentsp
			ORDER BY myorder
			UNION
			select top 1 [kdeleteflg], [date1], [kbranch], [eqpigrp], [kmodel], [udesc], [uprcpbase], [uprcpbas01], [uprcppct], [uamount], [uamount01], [uamount02], [uamount03], [uamount04], [uamount05], [uamount06], [eqpraexcnt], [eqpraexc01], [eqpraexc02], [eqpraexc03], [eqpraexc04], [eqpraexc05], [usummary], [utaxexcpt], [oetrancode], [glco1], [glacct1], [glbr1], [gldpt1], [glco2], [glacct2], [glbr2], [gldpt2], [glco3], [glacct3], [glbr3], [gldpt3], [glco4], [glacct4], [glbr4], [gldpt4], [glco5], [glacct5], [glbr5], [gldpt5], [glco6], [glacct6], [glbr6], [gldpt6], [uprrprod], [uprrdesc2], [uprrdesc3], [uprrconpro], [uprrfrdte], [uprrtodte], [uprrcondes], [uprroper], [uprraddday], [uprraddwk], [uprraddmo], [uprrlngtrm], [uprrtravel], [uprrunaday], [uprrunawk], [uprrunamo], [uprpromrt], [uprpromr01], [uprpromr02], [uprpromr03], [uprpromr04], [uprpromr05], [uprpromr06], [uprpromr07], [uprpromr08], [uprpromr09], [uprpromhrs], [uprpromh01], [uprpromh02], [uprpromh03], [uprpromh04], [uprpromh05], [uprpromot], [uprpromo01], [uprpromo02], [uprprobase], [uprproba01] from #uprrent
			ORDER BY myorder
		)
		INSERT INTO #uprrentmaster
			select top 1 [kdeleteflg], [date1], [kbranch], [eqpigrp], [kmodel], [udesc], [uprcpbase], [uprcpbas01], [uprcppct], @daily, @weekly, @monthly, @hourly, [uamount04], [uamount05], [uamount06], [eqpraexcnt], [eqpraexc01], [eqpraexc02], [eqpraexc03], [eqpraexc04], [eqpraexc05], [usummary], [utaxexcpt], [oetrancode], [glco1], [glacct1], [glbr1], [gldpt1], [glco2], [glacct2], [glbr2], [gldpt2], [glco3], [glacct3], [glbr3], [gldpt3], [glco4], [glacct4], [glbr4], [gldpt4], [glco5], [glacct5], [glbr5], [gldpt5], [glco6], [glacct6], [glbr6], [gldpt6], [uprrprod], [uprrdesc2], [uprrdesc3], [uprrconpro], [uprrfrdte], [uprrtodte], [uprrcondes], [uprroper], [uprraddday], [uprraddwk], [uprraddmo], [uprrlngtrm], [uprrtravel], [uprrunaday], [uprrunawk], [uprrunamo], [uprpromrt], [uprpromr01], [uprpromr02], [uprpromr03], [uprpromr04], [uprpromr05], [uprpromr06], [uprpromr07], [uprpromr08], [uprpromr09], [uprpromhrs], [uprpromh01], [uprpromh02], [uprpromh03], [uprpromh04], [uprpromh05], [uprpromot], [uprpromo01], [uprpromo02], [uprprobase], [uprproba01]
			from QRY

		SELECT @pbase = uprcpbase FROM #uprrentmaster
		SELECT * FROM #uprrentmaster
	END

	
	SET NOCOUNT OFF
END
" },
        {"sfProcessingQueue", @$"
IF OBJECT_ID('sfProcessingQueue', 'U') IS NULL
	CREATE TABLE sfProcessingQueue (
		Id INT IDENTITY(1,1) PRIMARY KEY,
		QuoteId NVARCHAR(36) NOT NULL,
		Status NVARCHAR(MAX) NOT NULL,
		ModifiedAt DATETIME NOT NULL DEFAULT GETDATE()
	);
"},
        {"sfEquipStatusChanges", @$"
IF OBJECT_ID('sfEquipStatusChanges', 'U') IS NULL
	create table sfEquipStatusChanges (
		[sfEquipStatusChanges_id] [int] IDENTITY(1,1) NOT NULL,
		[Id] [nvarchar](18) NOT NULL,
		[kequipnum] [varchar](12) NULL,
		[NewStatus] [varchar](2) NULL,
		[OldStatus] [varchar](2) NULL,
		[ModifiedDate] [datetime2](7) NULL,
		CONSTRAINT [PK_sfEquipStatusChanges] PRIMARY KEY CLUSTERED ( [sfEquipStatusChanges_id] ASC ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
	)
"},
        {"add_cols", @$"
IF COL_LENGTH('sfQuoteLineItem','ReservedEquipIds') IS NULL 
	alter table sfQuoteLineItem add ReservedEquipIds nvarchar(500) NULL
IF COL_LENGTH('sfQuote','kordnum') IS NULL 
	alter table sfQuote add kordnum int NULL
"},
        {"sp_ebs_unreserve_equip", @$"
CREATE OR ALTER PROCEDURE sp_ebs_unreserve_equip (@kequipnum VARCHAR(12), @lineId nvarchar(18))
AS
BEGIN
	DECLARE @ret int = 0
	SET XACT_ABORT ON
	BEGIN TRY
		BEGIN TRANSACTION
			insert into sfEquipStatusChanges (Id, kequipnum, NewStatus, OldStatus, ModifiedDate)
				select top 1 @lineId, @kequipnum, 'AV', e.eqpstatus, getdate()
				from equip e 
				where e.kequipnum = @kequipnum
			;WITH equipView AS (select top 1 * from equip WHERE kequipnum = @kequipnum and eqpstatus = 'RE')
				UPDATE equipView SET eqpstatus = 'AV'
		COMMIT TRANSACTION
		SET @ret = 1
	END TRY
	BEGIN CATCH
		IF (@@TRANCOUNT > 0) ROLLBACK TRANSACTION;
		SET @ret = 0
	END CATCH
	SET XACT_ABORT OFF
	SELECT @ret
END
"},
        {"sp_ebs_reserve_equip", @$"
CREATE OR ALTER PROCEDURE sp_ebs_reserve_equip (@kequipnum VARCHAR(12), @lineId nvarchar(18))
AS
BEGIN
	insert into sfEquipStatusChanges (Id, kequipnum, NewStatus, OldStatus, ModifiedDate)
		select top 1 @lineId, @kequipnum, 'RE', e.eqpstatus, getdate()
		from equip e 
		where e.kequipnum = @kequipnum

	UPDATE equip SET eqpstatus='RE' WHERE kequipnum = @kequipnum and eqpstatus = 'AV'
END
"},
		{"sp_ebs_sf_update_reservations", @$"
CREATE OR ALTER PROCEDURE sp_ebs_sf_update_reservations (@lineId nvarchar(18))
AS
BEGIN
	DECLARE @ret varchar(max) = ''
	SET XACT_ABORT ON
	BEGIN TRY
		BEGIN TRANSACTION
			declare @tbl table (kequipnum varchar(12))
			declare @step int = 0, @steps int = 0
			declare @ids varchar(max), @qty decimal(12,2) = 0, @pid varchar(18), @branch varchar(3), @status varchar(80), @qid nvarchar(18), @qname nvarchar(510)

			select top 1 @ids = NULLIF(ReservedEquipIds,''), 
				@qty = CASE WHEN IsDeleted = 1 OR q.Status in ('Draft','Needs Review','In review','Rejected','Denied') THEN 0 
					ELSE Quantity 
					END, 
				@pid = Product2Id,
				@qid = sfQuoteLineItem.QuoteId,
				@qname = q.Name,
				@branch = left(q.Branch__c, 3),
				@status = q.Status
			from sfQuoteLineItem 
			cross apply (select top 1 Name, Status, Branch__c from sfQuote where sfQuoteLineItem.QuoteId = Id) q
			where Id = @lineId
			
			declare @currentequips table (kequipnum varchar(max), rownum int)
			insert into @currentequips SELECT value, ROW_NUMBER() OVER(ORDER BY (SELECT 0)) as rownum FROM STRING_SPLIT(NULLIF(@ids,''), ',')
			insert into @tbl           SELECT * FROM STRING_SPLIT(NULLIF(@ids,''), ',')

			set @steps = (select @qty - (select count(*) from @currentequips))

			if (@steps > 0)
			begin
				declare @temp table (kequipnum varchar(12))
				declare @newids table (kequipnum varchar(12))
				while (@step < @steps)
				BEGIN
					delete from @temp
					;with q as (
						select top 1 COALESCE(e.kequipnum, e2.kequipnum) as kequipnum
						from sfProduct2 p 
						inner join utgrpprod u on p.Product_Group__c = u.eqpigrp and p.Product_Code__c = u.gmmottype
						outer apply (
							select top 1 e.kequipnum, e.eqprecdt
							from equip e
							left join @tbl t on e.kequipnum = t.kequipnum
							where eqpigrp = u.eqpigrp 
								and gmmottype = u.gmmottype 
								and eqpstatus = 'AV'
								and eqpphybr = @branch
								and t.kequipnum IS NULL
						) e
						outer apply (
							select top 1 e.kequipnum, e.eqprecdt
							from equip e
							left join @tbl t on e.kequipnum = t.kequipnum
							where eqpigrp = u.eqpigrp 
								and gmmottype = u.gmmottype 
								and eqpstatus = 'AV'
								and eqpphybr <> @branch
								and t.kequipnum IS NULL
						) e2
						where p.Id = @pid
						order by e.eqprecdt
					)
					insert into @temp select kequipnum from q
					insert into @tbl select kequipnum from @temp
					insert into @newids select kequipnum from @temp

					SET @step = @step + 1
				END

				insert into sfEquipStatusChanges (Id, kequipnum, NewStatus, OldStatus, ModifiedDate)
					select @lineId, e.kequipnum, 'RE', 'AV', getdate()
					from @newids e 

				;WITH equipView AS (
					select e.*
					from @newids t
					inner join equip e on t.kequipnum = e.kequipnum
					WHERE e.eqpstatus = 'AV'
				)
				UPDATE equipView SET eqpstatus = 'RE'

				select @ret = ISNULL(STRING_AGG(kequipnum, ','),'') from @tbl
				if (len(@ret) > 0)
					update sfQuoteLineItem set ReservedEquipIds = @ret where Id = @lineId

				IF ISNULL(@ids,'') <> @ret
					INSERT INTO sfProcessingNotifications (EventType, RecordId, Title, Body)
						SELECT 'Equip_Status_Updated', @qid, N'EBS Equip Status Updated', N'Equipment status set to ""RE"" for: '+REPLACE(@ret,',',', ')
			end
			else if (@steps < 0)
			begin
				declare @del table (kequipnum varchar(12))
				delete from @del
				while (@step > @steps)
				BEGIN
					declare @eid varchar(12) = (select top 1 kequipnum from @currentequips order by rownum desc)
					insert into @del 
						select @eid
					delete from @currentequips where kequipnum = @eid
					SET @step = @step - 1
				END

				insert into sfEquipStatusChanges (Id, kequipnum, NewStatus, OldStatus, ModifiedDate)
					select @lineId, d.kequipnum, 'AV', e.eqpstatus, getdate()
					from @del d 
					inner join equip e on d.kequipnum = e.kequipnum

				;WITH equipView AS (
					select e.*
					from @del t
					inner join equip e on t.kequipnum = e.kequipnum
					WHERE e.eqpstatus = 'RE'
				)
				UPDATE equipView SET eqpstatus = 'AV'
				
				declare @strids varchar(max), @delstrids varchar(max)
				select @strids = ISNULL(STRING_AGG(c.kequipnum, ','),'') 
				from @currentequips c
				left join @del d on d.kequipnum = c.kequipnum
				where d.kequipnum IS NULL

				select @delstrids = ISNULL(STRING_AGG(kequipnum, ','),'') from @del

				if (len(isnull(@strids,'')) > 0)
					update sfQuoteLineItem set ReservedEquipIds = @strids where Id = @lineId
				else
					update sfQuoteLineItem set ReservedEquipIds = NULL where Id = @lineId

				IF ISNULL(@ids,'') <> @strids
					INSERT INTO sfProcessingNotifications (EventType, RecordId, Title, Body)
						SELECT 'Equip_Status_Updated', @qid, N'EBS Equip Status Updated', N'Equipment status restored to ""AV"" for: '+REPLACE(@delstrids,',',', ')

				select @ret = ISNULL(@strids,'')
			end
			else
			begin
				select @ret = ISNULL(STRING_AGG(kequipnum, ','),'') from @tbl
			end
		COMMIT TRANSACTION

	END TRY
	BEGIN CATCH
		IF (@@TRANCOUNT > 0) ROLLBACK TRANSACTION;
		SET @ret = ''
	END CATCH
	SET XACT_ABORT OFF
	SELECT @ret ret
END
" },
		{"sfProcessingNotifications", @$"
IF OBJECT_ID('sfProcessingNotifications','U') IS NULL
BEGIN
	CREATE TABLE sfProcessingNotifications (
		Id          INT IDENTITY PRIMARY KEY,
		EventType   NVARCHAR(50)  NOT NULL,
		RecordId    NVARCHAR(50)  NULL,
		Title       NVARCHAR(100) NULL,
		Body        NVARCHAR(MAX) NULL,
		Payload     NVARCHAR(MAX) NULL,
		CreatedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
		IsProcessed BIT           NOT NULL DEFAULT 0,
		ProcessedAt DATETIME2 NULL
	)
	CREATE INDEX IX_sfProcessingNotifications_IsProcessed ON sfProcessingNotifications(IsProcessed, CreatedAt);
END
"},
        {"trg_oehead_ProcessingComplete", @$"
CREATE OR ALTER TRIGGER trg_oehead_ProcessingComplete
ON oehead
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(kswoseg)
    BEGIN
        INSERT INTO sfProcessingNotifications (EventType, RecordId, Title, Body, Payload)
			SELECT 'Order_Created', q.Id, N'Order Created', N'Order number '+cast(i.kordnum as nvarchar(max))+' has been created in EBS.',
				   (SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER)
			FROM inserted i
			INNER JOIN deleted d ON i.kordnum = d.kordnum and i.kbranch = d.kbranch
			cross apply( select top 1 Id from sfQuote where kordnum = i.kordnum and Branch__c = i.kbranch ) q
			WHERE i.kswoseg = 0 AND d.kswoseg = -999;
    END
END
"}
    };
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var sp in spdefs)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sp.Value);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to update procedure: {sp.Key}");
                throw;
            }
        }
    }

    app.UseSwagger(c =>
    {
        c.RouteTemplate = "swagger/{documentName}/swagger.json";
    });
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("./swagger/v1/swagger.json", "Quote Integration API v1");
        c.RoutePrefix = string.Empty; // Swagger UI at root "/"
    });

    app.MapQuoteEndpoints();
    app.MapLogEndpoints();
    app.MapGet("/health", () => Results.Ok(new
    {
        Status = "Healthy",
        Timestamp = DateTime.UtcNow,
        Service = "SalesforceQuoteIntegration"
    })).WithTags("Health").WithSummary("Health check endpoint for IIS preload and uptime monitoring");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Service terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
