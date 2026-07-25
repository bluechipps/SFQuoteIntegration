using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using SalesforceQuoteIntegration.Models;
using AutoMapper;

namespace SalesforceQuoteIntegration.Services;

/// <summary>
/// Executes SOQL queries against the Salesforce REST API.
/// Inject this service wherever you need to react to CDC events with a follow-up query.
/// </summary>
public class SalesforceQueryService
{
    private readonly SalesforceAuthService _authService;
    private readonly QuoteStorageService _storageService;
    private readonly RawSqlService _rawSql;
    private readonly HttpClient _httpClient;
    private readonly IMapper _mapper;
    private const string ApiVersion = "59.0";

    // In-memory cache of CustomNotificationType IDs by their API name.
    // Avoids re-querying Salesforce for the same type on every notification send.
    private readonly Dictionary<string, string> _notifTypeIdCache = new();

    public SalesforceQueryService(
        SalesforceAuthService authService,
        QuoteStorageService storageService,
        RawSqlService rawSql,
        IMapper mapper
        )
    {
        _authService    = authService;
        _storageService = storageService;
        _rawSql         = rawSql;
        _mapper         = mapper;


        var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = false
        };
        _httpClient = new HttpClient(handler);
    }

    public async Task<List<JObject>> QueryAsync(string soql)
    {
        var (token, instanceUrl) = await _authService.GetTokenAsync();

        var encoded  = Uri.EscapeDataString(soql);
        var url      = $"{instanceUrl}/services/data/v{ApiVersion}/queryAll/?q={encoded}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error($"SOQL query failed [{(int)response.StatusCode}]: {error} | Query: {soql}");
            throw new Exception($"SOQL query failed ({response.StatusCode}): {error}");
        }

        var json   = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<JObject>(json);

        var records = result?["records"] as JArray ?? new JArray();
        bool bDone = result?["done"]?.Value<bool>() ?? true;
        while (!bDone)
        {
            string nextRecordsUrl = result?["nextRecordsUrl"]?.Value<string>() ?? "";
            if (string.IsNullOrEmpty(nextRecordsUrl))
            {
                Log.Warning($"SOQL query returned partial results but no nextRecordsUrl was provided. Records returned: {records.Count}. Query: {soql}");
                break;
            }
            var nextRequest = new HttpRequestMessage(HttpMethod.Get, $"{instanceUrl}{nextRecordsUrl}");
            nextRequest.Headers.Add("Authorization", $"Bearer {token}");
            var nextResponse = await _httpClient.SendAsync(nextRequest);
            if (!nextResponse.IsSuccessStatusCode)
            {
                var error = await nextResponse.Content.ReadAsStringAsync();
                Log.Error($"SOQL query failed on nextRecordsUrl [{(int)nextResponse.StatusCode}]: {error} | Query: {soql}");
                throw new Exception($"SOQL query failed on nextRecordsUrl ({nextResponse.StatusCode}): {error}");
            }
            var nextJson = await nextResponse.Content.ReadAsStringAsync();
            result = JsonConvert.DeserializeObject<JObject>(nextJson);
            var nextRecords = result?["records"] as JArray ?? new JArray();
            records.Merge(nextRecords);
            bDone = result?["done"]?.Value<bool>() ?? true;
        }

        Log.Information($"SOQL query completed. Records returned: {records.Count}. Query: {soql}");

        return records.Cast<JObject>().ToList();
    }

    public async Task<List<T>> QueryAsync<T>(string soql)
    {
        var rows = await QueryAsync(soql);
        return rows
            .Select(r => r.ToObject<T>())
            .Where(r => r != null)
            .Cast<T>()
            .ToList();
    }

    public async Task<JObject?> GetRecordAsync(string objectType, string recordId)
    {
        var (token, instanceUrl) = await _authService.GetTokenAsync();

        var url = $"{instanceUrl}/services/data/v{ApiVersion}/sobjects/{objectType}/{recordId}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {token}");

        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error($"GetRecord failed [{(int)response.StatusCode}]: {error} | {objectType}/{recordId}");
            throw new Exception($"GetRecord failed ({response.StatusCode}): {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<JObject>(json);
    }

    // True return value indicates that we should continue checking for unprocessed changes
    public async Task<bool> HandleUnprocessedQuoteAsync(ChangeEventRecord? chrec)
    {

        string kequipnum = "";
        string sql = "";
        if (chrec == null)
        {
            Log.Error($"Critical error. HandleUnprocessedQuoteAsync received a null change record.");
            return false;
        }

        //decimal? ToDec(object? v) => v is null || v == DBNull.Value ? null : Convert.ToDecimal(v);

        //ChangeEventRecord? chrec;
        //try
        //{
        //    chrec = await _storageService.GetNextUnprocessedChangeAsync();
        //    if (chrec != null)
        //    {
        //        Log.Information($"Quote {chrec.Name} ({chrec.SalesforceRecordId}) — ready to process.");
        //    }
        //    else
        //    {
        //        Log.Information($"No unprocessed quote records found.");
        //        return false;
        //    }
        //}
        //catch (Exception ex)
        //{
        //    Log.Error($"Critical error retrieving unprocessed Quote change event: {ex.Message}\r\n{ex.StackTrace}");
        //    return true;
        //}

        try
        {
            var rechead = await _storageService.GetQuoteByIdAsync(chrec.SalesforceRecordId);
            if (rechead == null)
            {
                Log.Warning($"Quote {chrec.Name} ({chrec.SalesforceRecordId}) not found in local database");
                await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"Quote {chrec.Name} ({chrec.SalesforceRecordId}) not found in local database");
                
                // True indicates that we should continue checking for unprocessed changes
                return true;
            }

            List<sfQuoteLineItem> recdet = [];
            recdet = await _storageService.GetQuoteLinesByIdAsync(chrec.SalesforceRecordId);
            if (recdet.Count == 0)
            {
                // update change event record to indicate processing error
                Log.Warning($"Quote line items for quote {chrec.Name} ({chrec.SalesforceRecordId}) not found");
                await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"Quote line items for quote {chrec.Name} ({chrec.SalesforceRecordId}) not found");
                return true;
            }

            string sqlProducts = "";
            List<sfProduct2custom> products2 = [];
            foreach (var line in recdet)
            {
                Log.Information($"Quote {chrec.Name} ({chrec.SalesforceRecordId}) line item: {line.Product2Id} | Qty: {line.Quantity} | Price: {line.UnitPrice}");
                sfProduct2? prd = await _storageService.GetProductByIdAsync(line.Product2Id);
                if (prd != null)
                {
                    sfProduct2custom pr = _mapper.Map<sfProduct2custom>(prd);

                    //RYAN - TODO - add misc support
                    if (pr.Family == "Misc Charges")
                    {
                        var recs = await _rawSql.ExecuteReaderAsync(@$"
select top 1 *
from sfProduct2 p 
inner join utmisc u on p.Product_Group__c = u.kmfg and u.kbranch = '{rechead.Branch__c}' and u.oeptype = '2'
cross apply (select top 1 custpcl from custmast where kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000') cm
where p.Id = '{pr.Id}'
order by e.eqprecdt
");
                        if (recs.Count > 0)
                        {
                            kequipnum = recs[0]["kequipnum"]?.ToString() ?? "";
                            pr.ProductCode = kequipnum;
                            pr.Description = recs[0]["udesc"]?.ToString() ?? pr.Description;
                            pr.QuoteLineItemId = line.Id;
                            pr.UnitPrice = line.UnitPrice;
                            pr.Quantity = line.Quantity;
                            products2.Add(pr);
                            Log.Information($"Found available equipment {kequipnum} for quote {chrec.Name} ({chrec.SalesforceRecordId})");
                        }
                        else
                        {
                            //RYAN - TODO - alert user
                            kequipnum = "";
                            products2.Add(pr);
                            Log.Error($"No available equipment found for quote {chrec.Name} ({chrec.SalesforceRecordId})");
                            await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"No available equipment found for quote {chrec.Name} ({chrec.SalesforceRecordId})");
                            return true;
                        }
                    }
                    else
                    {
                        var recs = await _rawSql.ExecuteReaderAsync(@$"
;with qry as (
    select top 1 COALESCE(e.kequipnum, e2.kequipnum) as kequipnum, u.udesc as udesc
    from sfProduct2 p 
    inner join utgrpprod u on p.Product_Group__c = u.eqpigrp and p.Product_Code__c = u.gmmottype
    outer apply (
	    select top 1 * 
	    from equip 
	    where eqpigrp = u.eqpigrp 
		    and gmmottype = u.gmmottype 
		    and eqpstatus = 'AV'
		    and eqpphybr = '{rechead.Branch__c}'
    ) e
    outer apply (
	    select top 1 * 
	    from equip 
	    where eqpigrp = u.eqpigrp 
		    and gmmottype = u.gmmottype 
		    and eqpstatus = 'AV'
		    and eqpphybr <> '{rechead.Branch__c}'
    ) e2
    where p.Id = '{pr.Id}'
    order by e.eqprecdt
)
select top 1 e.*, q.udesc
from equip e
inner join qry q on e.kequipnum = q.kequipnum
");
                        if (recs.Count > 0)
                        {
                            kequipnum = recs[0]["kequipnum"]?.ToString() ?? "";
                            pr.ProductCode = kequipnum;
                            pr.Description = recs[0]["udesc"]?.ToString() ?? pr.Description;
                            pr.QuoteLineItemId = line.Id;
                            pr.UnitPrice = line.UnitPrice;
                            pr.Quantity = line.Quantity;
                            //pr.UnitPrice = ToDec(recs[0]["UnitPrice"]) ?? pr.UnitPrice;
                            //pr.Quantity = ToDec(recs[0]["Quantity"]) ?? pr.Quantity;
                            //pr.QuoteLineItemId = recs[0]["QuoteLineItemId"]?.ToString() ?? pr.QuoteLineItemId;
                            products2.Add(pr);
                            Log.Information($"Found available equipment {kequipnum} for quote {chrec.Name} ({chrec.SalesforceRecordId})");
                        }
                        else
                        {
                            //RYAN - TODO - alert user
                            kequipnum = "";
                            products2.Add(pr);
                            Log.Error($"No available equipment found for quote {chrec.Name} ({chrec.SalesforceRecordId})");
                            await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"No available equipment found for quote {chrec.Name} ({chrec.SalesforceRecordId})");
                            return true;
                        }
                    }
                    
                }
                else
                {
                    //RYAN - TODO - alert user
                    Log.Warning($"Product {line.Product2Id} not found in local database. Parent quote: {chrec.Name} ({chrec.SalesforceRecordId})");
                    await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"Product {line.Product2Id} not found in local database. Parent quote: {chrec.Name} ({chrec.SalesforceRecordId})");
                    return true;
                }
            }


            //scalar query for kordnum
            var newKordnum = await _rawSql.ExecuteScalarAsync<int>(@$"
DECLARE @neword Int = 0
BEGIN TRY
	BEGIN TRANSACTION
	SELECT TOP 1 @neword = kordnum + 1 FROM utbr WHERE kbranch = '{rechead.Branch__c}' ORDER BY kbranch,utbr_id
	UPDATE utbr SET kordnum = @neword WHERE kbranch = '{rechead.Branch__c}'
	COMMIT TRANSACTION
    SELECT @neword
END TRY
BEGIN CATCH
	ROLLBACK TRANSACTION
    SELECT @neword
END CATCH
");
            if (newKordnum > 0)
            {
                Log.Information($"Retrieved new kordnum: {newKordnum}");
            }
            else
            {
                
                Log.Error($"Failed to retrieve new kordnum for branch {rechead.Branch__c}. Parent quote: {chrec.Name} ({chrec.SalesforceRecordId})");
                await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"Failed to retrieve new kordnum for branch {rechead.Branch__c}.");
                return true;
            }

            int itemnum = 1;
            foreach (var p in products2)
            {
                sqlProducts += $@"
DELETE FROM #pricing
INSERT INTO #pricing
    exec sp_ebs_rental_pricing @kcustnum='{rechead.EBS_Customer_ID__c}',@custsnum='000',@kbranch='{rechead.Branch__c}',@equipid='{p.ProductCode}'

;with c as (
	select top 1 * from custmast where kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000'
)
INSERT INTO wk_oed01_admin99999_88888 ([kdeleteflg],[oeptype],[oepordnum],[ktermid] ,[kbranch],            [key_3_a1],[kmfg],[kpart]                 ,[custpcl],[custrcl],[custecl],[custlcl],[oepqtyord],[oeqtyship],[pmdesc],[iclocmain], [key_8_a1],                   [key_8_a101],[key_8_a102],[icstatus],[oepsell],  [oetrancode],[oetaxex],[iccost],[pmcommod],[pmum],[kordnum],   [oefrexecpt],[oenetdtl],[oefactor],[icdlrcl],[iclocsec],                 [icqtyonord],[pmret],[eqpmeter],[eqpmeter01],[pmrep],[pmmcl],[pmpriceid],[artotal],[artotal01],[artotal02],[artotal03],[artotal04],[artotal05],[artotal06],[artotal07],[artotal08],[artotal09],[artotal10],[artotal11],[oeitemnum],[icbrchg],[dummy],[dummy01],[dummy02],[dummy03],[dummy04],[dummy05],[dummy06],[dummy07],[dummy08],[dummy09],[oerentot],[oerenthr],[oerentwk] ,[oerentday],[oerentmnth],[kmodel],[date1]            ,[apdateons]        ,[eqpigrp],                                                                                                    [oecomplete],[action],[shift],[key_4_a1],[uprpromrt],[uprpromr01],[uprpromr02],[uprpromr03],[uprpromr04],[uprpromr05],[uprpromr06],[uprpromr07],[uprpromr08],[uprpromr09],[wonotes],[oeshipclas],[glco1],[glacct1],[glbr1],[gldpt1],[recnum],[recnum01],[recnum02],[recnum03],[recnum04],[recnum05],[recnum06],[recnum07],[recnum08],[recnum09],[key_10_a1],[key_10_a01],[key_10_a02],[key_10_a03],[key_10_a04],[key_10_a05],[key_10_a06],[key_10_a07],[key_10_a08],[key_10_a09],[key_2_a1],[key_2_a101],[key_2_a102],[key_2_a103],[key_2_a104],[key_2_a105],[key_2_a106],[key_2_a107],[key_2_a108],[key_2_a109],[amtlast],[amtlast01],[amtlast02],[amtlast03],[amtlast04],[amtlast05],[amtlast06],[amtlast07],[amtlast08],[amtlast09],[curractdt],[curractd01],[curractd02],[curractd03],[curractd04],[curractd05],[curractd06],[curractd07],[curractd08],[curractd09],[pmset])
SELECT                                 'N'         ,'4'      ,1          ,'admin999','{rechead.Branch__c}','YES'     ,''    ,'{p.ProductCode ?? ""}',''        ,c.custrcl,''       ,''       ,1          ,1          ,u.udesc ,''          ,'{p.Product_Code__c ?? ""}'  ,''          ,''          ,''        ,p.uamount02,''          ,''       ,0       ,''        ,''    ,{newKordnum},''          ,''        ,'N'       ,''       ,'{p.Product_Code__c ?? ""}',0           ,''     ,'0'       ,'0'         ,''     ,''     ,'0'        ,0        ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,{itemnum}  ,''       ,''     ,''       ,''       ,''       ,''       ,''       ,''       ,''       ,''       ,''       ,0         ,0         ,p.uamount01,p.uamount  ,p.uamount02 ,e.kmodel  ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','{p.Product_Group__c ?? ""}'     ,'R'         ,'4'     ,'R'    ,''        ,p.uamount  ,p.uamount01 ,p.uamount02 ,0           ,0           ,0           ,0           ,0           ,0           ,p.uamount   ,''       ,''          ,''     ,''       ,''     ,''      ,1       ,0         ,0         ,0         ,0         ,0         ,0         ,0         ,0         ,0         ,''         ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''        ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,0        ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,NULL       ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,'0'
FROM c
CROSS APPLY ( select top 1 * from equip where kequipnum = '{p.ProductCode}') e
CROSS APPLY ( select top 1 udesc from utgrpprod where eqpigrp = '{p.Product_Group__c ?? ""}' and gmmottype = '{p.Product_Code__c ?? ""}') u
OUTER APPLY ( select top 1 * from #pricing ) p


";
                itemnum++;
            }

            sql = @$"
drop table if exists wk_oeh01_admin99999_88888;
drop table if exists wk_oed01_admin99999_88888;
drop table if exists wk_eqm01_admin99999_88888;
CREATE TABLE [dbo].[wk_oeh01_admin99999_88888] ( [wk_oeh01_admin99999_88888_id] [int] IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_wk_oeh01_admin99999_88888] PRIMARY KEY (wk_oeh01_admin99999_88888_id), [kdeleteflg] [varchar] (1) NULL, [oepordnum] [int] NULL, [udlrcode] [varchar] (3) NULL, [kdoctype] [varchar] (1) NULL, [oetypeord] [varchar] (1) NULL, [ktermid] [varchar] (8) NULL, [ktermid1] [varchar] (8) NULL, [ktermid2] [varchar] (8) NULL, [ktermid3] [varchar] (8) NULL, [username] [varchar] (8) NULL, [kbranch] [varchar] (3) NULL, [kcustnum] [varchar] (8) NULL, [custpcl] [varchar] (2) NULL, [custrcl] [varchar] (2) NULL, [custecl] [varchar] (2) NULL, [custlcl] [varchar] (2) NULL, [custphone] [varchar] (16) NULL, [custstdpo] [varchar] (30) NULL, [oecontact] [varchar] (30) NULL, [custsnum] [varchar] (3) NULL, [taxcodes] [varchar] (10) NULL, [kmake] [varchar] (3) NULL, [kmodel] [varchar] (12) NULL, [kserialnum] [varchar] (15) NULL, [kequipnum] [varchar] (12) NULL, [oedept] [varchar] (3) NULL, [oeslsrep] [varchar] (3) NULL, [custptrm] [smallint] NULL, [oeptype] [varchar] (1) NULL, [custdisad1] [varchar] (3) NULL, [custdisad2] [varchar] (3) NULL, [custdisad3] [varchar] (3) NULL, [custshpvia] [varchar] (20) NULL, [custtcstk] [varchar] (2) NULL, [oeshipname] [varchar] (35) NULL, [oeshipadd] [varchar] (35) NULL, [oeshipad01] [varchar] (35) NULL, [oeshipad02] [varchar] (35) NULL, [oeshipcity] [varchar] (25) NULL, [oeshipstat] [varchar] (2) NULL, [oeshipzip] [varchar] (10) NULL, [oelostsale] [varchar] (1) NULL, [oelostreas] [varchar] (1) NULL, [oecomplete] [varchar] (1) NULL, [oeprtpick] [varchar] (1) NULL, [oeprcpick] [varchar] (1) NULL, [oejobnum] [varchar] (20) NULL, [program] [varchar] (10) NULL, [kordnum] [int] NULL, [oedate] [datetime] NULL, [advbill] [varchar] (1) NULL, [oeonrent] [datetime] NULL, [action] [varchar] (1) NULL, [oelastbill] [datetime] NULL, [oenextbill] [datetime] NULL, [date1] [datetime] NULL, [ktermidlk] [varchar] (8) NULL, [ktermidlk1] [varchar] (8) NULL, [dummy1] [varchar] (1) NULL, [dummy101] [varchar] (1) NULL, [dummy102] [varchar] (1) NULL, [dummy103] [varchar] (1) NULL, [dummy104] [varchar] (1) NULL, [dummy105] [varchar] (1) NULL, [dummy106] [varchar] (1) NULL, [dummy107] [varchar] (1) NULL, [dummy108] [varchar] (1) NULL, [dummy109] [varchar] (1) NULL, [dummy110] [varchar] (1) NULL, [dummy111] [varchar] (1) NULL, [dummy112] [varchar] (1) NULL, [dummy113] [varchar] (1) NULL, [dummy114] [varchar] (1) NULL, [dummy115] [varchar] (1) NULL, [dummy116] [varchar] (1) NULL, [dummy117] [varchar] (1) NULL, [dummy118] [varchar] (1) NULL, [dummy119] [varchar] (1) NULL, [oecusteqid] [varchar] (12) NULL, [arinvno] [int] NULL, [oetaxex] [varchar] (1) NULL, [oetrancode] [varchar] (2) NULL, [artotal] [float] NULL, [artotal01] [float] NULL, [artotal02] [float] NULL, [artotal03] [float] NULL, [artotal04] [float] NULL, [artotal05] [float] NULL, [artaxamt] [float] NULL, [artaxamt01] [float] NULL, [artaxamt02] [float] NULL, [artaxamt03] [float] NULL, [artaxamt04] [float] NULL, [artaxamt05] [float] NULL, [recnum] [int] NULL, [crcardco] [varchar] (5) NULL, [crcardno] [varchar] (20) NULL, [ccexpdate] [datetime] NULL, [ccauth] [varchar] (10) NULL, [drlicst] [varchar] (2) NULL, [drlicno] [varchar] (15) NULL, [vehlicst] [varchar] (2) NULL, [vehlicno] [varchar] (10) NULL, [exretdt] [datetime] NULL, [rentstart] [datetime] NULL CONSTRAINT wk_oeh01_admin99999_88888_id_0 UNIQUE NONCLUSTERED(oepordnum) ) 
CREATE TABLE [dbo].[wk_oed01_admin99999_88888] ( [wk_oed01_admin99999_88888_id] [int] IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_wk_oed01_admin99999_88888] PRIMARY KEY (wk_oed01_admin99999_88888_id), [kdeleteflg] [varchar] (1) NULL, [oeptype] [varchar] (1) NULL, [oepordnum] [int] NULL, [ktermid] [varchar] (8) NULL, [kbranch] [varchar] (3) NULL, [key_3_a1] [varchar] (3) NULL, [kmfg] [varchar] (3) NULL, [kpart] [varchar] (20) NULL, [custpcl] [varchar] (2) NULL, [custrcl] [varchar] (2) NULL, [custecl] [varchar] (2) NULL, [custlcl] [varchar] (2) NULL, [oepqtyord] [real] NULL, [oeqtyship] [real] NULL, [pmdesc] [varchar] (40) NULL, [iclocmain] [varchar] (8) COLLATE SQL_Latin1_General_Cp437_Bin NULL, [key_8_a1] [varchar] (8) NULL, [key_8_a101] [varchar] (8) NULL, [key_8_a102] [varchar] (8) NULL, [icstatus] [varchar] (2) NULL, [oepsell] [float] NULL, [oetrancode] [varchar] (2) NULL, [oetaxex] [varchar] (1) NULL, [iccost] [real] NULL, [pmcommod] [varchar] (4) NULL, [pmum] [varchar] (2) NULL, [kordnum] [int] NULL, [oefrexecpt] [varchar] (1) NULL, [oenetdtl] [varchar] (1) NULL, [oefactor] [varchar] (1) NULL, [icdlrcl] [varchar] (3) NULL, [iclocsec] [varchar] (8) NULL, [icqtyonord] [int] NULL, [pmret] [varchar] (1) NULL, [eqpmeter] [smallint] NULL, [eqpmeter01] [smallint] NULL, [pmrep] [varchar] (1) NULL, [pmmcl] [varchar] (3) NULL, [pmpriceid] [smallint] NULL, [artotal] [float] NULL, [artotal01] [float] NULL, [artotal02] [float] NULL, [artotal03] [float] NULL, [artotal04] [float] NULL, [artotal05] [float] NULL, [artotal06] [float] NULL, [artotal07] [float] NULL, [artotal08] [float] NULL, [artotal09] [float] NULL, [artotal10] [float] NULL, [artotal11] [float] NULL, [oeitemnum] [smallint] NULL, [icbrchg] [varchar] (3) NULL, [dummy] [varchar] (1) NULL, [dummy01] [varchar] (1) NULL, [dummy02] [varchar] (1) NULL, [dummy03] [varchar] (1) NULL, [dummy04] [varchar] (1) NULL, [dummy05] [varchar] (1) NULL, [dummy06] [varchar] (1) NULL, [dummy07] [varchar] (1) NULL, [dummy08] [varchar] (1) NULL, [dummy09] [varchar] (1) NULL, [oerentot] [float] NULL, [oerenthr] [float] NULL, [oerentwk] [float] NULL, [oerentday] [float] NULL, [oerentmnth] [float] NULL, [kmodel] [varchar] (12) NULL, [date1] [datetime] NULL, [apdateons] [datetime] NULL, [eqpigrp] [varchar] (3) NULL, [oecomplete] [varchar] (1) NULL, [action] [varchar] (1) NULL, [shift] [varchar] (1) NULL, [key_4_a1] [varchar] (4) NULL, [uprpromrt] [real] NULL, [uprpromr01] [real] NULL, [uprpromr02] [real] NULL, [uprpromr03] [real] NULL, [uprpromr04] [real] NULL, [uprpromr05] [real] NULL, [uprpromr06] [real] NULL, [uprpromr07] [real] NULL, [uprpromr08] [real] NULL, [uprpromr09] [real] NULL, [wonotes] [varchar] (70) NULL, [oeshipclas] [varchar] (20) NULL, [glco1] [varchar] (3) NULL, [glacct1] [varchar] (6) NULL, [glbr1] [varchar] (3) NULL, [gldpt1] [varchar] (3) NULL, [recnum] [int] NULL, [recnum01] [int] NULL, [recnum02] [int] NULL, [recnum03] [int] NULL, [recnum04] [int] NULL, [recnum05] [int] NULL, [recnum06] [int] NULL, [recnum07] [int] NULL, [recnum08] [int] NULL, [recnum09] [int] NULL, [key_10_a1] [varchar] (10) NULL, [key_10_a01] [varchar] (10) NULL, [key_10_a02] [varchar] (10) NULL, [key_10_a03] [varchar] (10) NULL, [key_10_a04] [varchar] (10) NULL, [key_10_a05] [varchar] (10) NULL, [key_10_a06] [varchar] (10) NULL, [key_10_a07] [varchar] (10) NULL, [key_10_a08] [varchar] (10) NULL, [key_10_a09] [varchar] (10) NULL, [key_2_a1] [varchar] (2) NULL, [key_2_a101] [varchar] (2) NULL, [key_2_a102] [varchar] (2) NULL, [key_2_a103] [varchar] (2) NULL, [key_2_a104] [varchar] (2) NULL, [key_2_a105] [varchar] (2) NULL, [key_2_a106] [varchar] (2) NULL, [key_2_a107] [varchar] (2) NULL, [key_2_a108] [varchar] (2) NULL, [key_2_a109] [varchar] (2) NULL, [amtlast] [float] NULL, [amtlast01] [float] NULL, [amtlast02] [float] NULL, [amtlast03] [float] NULL, [amtlast04] [float] NULL, [amtlast05] [float] NULL, [amtlast06] [float] NULL, [amtlast07] [float] NULL, [amtlast08] [float] NULL, [amtlast09] [float] NULL, [curractdt] [datetime] NULL, [curractd01] [datetime] NULL, [curractd02] [datetime] NULL, [curractd03] [datetime] NULL, [curractd04] [datetime] NULL, [curractd05] [datetime] NULL, [curractd06] [datetime] NULL, [curractd07] [datetime] NULL, [curractd08] [datetime] NULL, [curractd09] [datetime] NULL, [pmset] [smallint] NULL CONSTRAINT wk_oed01_admin99999_88888_id_0 UNIQUE NONCLUSTERED(oeitemnum,icbrchg) ) 
CREATE TABLE [dbo].[wk_eqm01_admin99999_88888] ( [wk_eqm01_admin99999_88888_id] [int] IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_wk_eqm01_admin99999_88888] PRIMARY KEY (wk_eqm01_admin99999_88888_id), [kequipnum] [varchar] (12) NULL, [kmfg] [varchar] (3) NULL, [kmodel] [varchar] (12) NULL, [kserialnum] [varchar] (15) NULL, [eqpigrp] [varchar] (3) NULL, [eqpsize] [varchar] (4) NULL, [eqptype] [varchar] (2) NULL, [eqpstatus] [varchar] (2) NULL, [eqpstatdt] [datetime] NULL, [eqpyrmfg] [varchar] (2) NULL, [eqpmeter] [smallint] NULL, [eqpphybr] [varchar] (3) NULL, [eqpdesc] [varchar] (40) NULL, [eqprntcust] [varchar] (8) NULL, [eqprano] [int] NULL, [eqponrdt] [datetime] NULL, [eqpoffrdt] [datetime] NULL, [eqpretno] [varchar] (10) NULL, [eqpsldcust] [varchar] (8) NULL, [eqpsldinv] [varchar] (8) NULL, [eqpsldamt] [float] NULL, [eqpslddate] [datetime] NULL, [eqpdimen] [varchar] (20) NULL, [eqpwarperd] [smallint] NULL, [eqpwarpts] [real] NULL, [eqpwarlab] [real] NULL, [eqpwaroth] [real] NULL, [eqpwarodt] [datetime] NULL, [eqprnthrly] [real] NULL, [eqpsellprc] [float] NULL, [eqpinsvalu] [real] NULL, [eqprntday] [real] NULL, [eqprntweek] [real] NULL, [eqprntmnth] [real] NULL, [eqprntover] [real] NULL, [eqporigcst] [float] NULL, [eqpfrtin] [real] NULL, [eqpinvrecd] [varchar] (12) NULL, [eqprecdt] [datetime] NULL, [eqpporec] [varchar] (30) NULL, [eqporddt] [datetime] NULL, [eqppurvend] [varchar] (8) NULL, [eqpfinvend] [varchar] (8) NULL, [eqpfinote] [varchar] (12) NULL, [eqpfinamt] [float] NULL, [eqpfinrate] [real] NULL, [eqpfinterm] [smallint] NULL, [eqpfindue] [datetime] NULL, [eqprepexp] [real] NULL, [eqprepcap] [real] NULL, [eqpwrtdn] [real] NULL, [eqptrnsfr] [real] NULL, [eqpcos] [float] NULL, [eqpinc] [float] NULL, [eqpyrrep] [real] NULL, [eqpyrinc] [real] NULL, [eqpyrdepr] [float] NULL, [eqpyrint] [real] NULL, [glco] [varchar] (3) NULL, [glacct] [varchar] (6) NULL, [glbr] [varchar] (3) NULL, [gldpt] [varchar] (3) NULL, [eqpfaexplf] [smallint] NULL, [eqpfaremlf] [smallint] NULL, [eqpfamethd] [varchar] (1) NULL, [eqpfarate] [real] NULL, [eqpfaamt] [real] NULL, [eqpfasalvg] [real] NULL, [eqpraexcnt] [smallint] NULL, [date1] [datetime] NULL, [action] [varchar] (1) NULL, [kdeleteflg] [varchar] (1) NULL, [dummy] [varchar] (1) NULL, [dummy01] [varchar] (1) NULL, [dummy02] [varchar] (1) NULL, [icqty] [int] NULL, [program] [varchar] (10) NULL, [username] [varchar] (8) NULL CONSTRAINT wk_eqm01_admin99999_88888_id_0 UNIQUE NONCLUSTERED(kdeleteflg,kequipnum) ) 
";
            Log.Information($"ExecuteNonQueryAsync: \r\n{sql}");
            var rows = await _rawSql.ExecuteNonQueryAsync(sql);
            sql = $@"
IF OBJECT_ID('wksf_writecom','U') IS NOT NULL DROP TABLE [dbo].[wksf_writecom];
CREATE TABLE [dbo].[wksf_writecom] ([1] [varchar] (100), [2] [varchar] (100), [3] [varchar] (100), [4] [varchar] (100), [5] [varchar] (100), [6] [varchar] (100), [7] [varchar] (100), [8] [varchar] (100), [9] [varchar] (100), [10] [varchar] (100), [11] [varchar] (100), [15] [varchar] (100), [20] [varchar] (100), [apply_to_inv] [varchar] (100), [dialup_company] [varchar] (100), [dialup_contact] [varchar] (100), [dialup_fax] [varchar] (100), [finance_cust] [varchar] (100), [finance_name] [varchar] (100), [got_contracts] [varchar] (100), [isQuote] [varchar] (100), [NewOrd] [varchar] (100), [OeMeter] [varchar] (100), [outofterr] [varchar] (100), [override_date] [varchar] (100), [Print_Part_Numbers] [varchar] (100), [Print_PartMfr_PL] [varchar] (100), [PrintPrices] [varchar] (100), [PrintSDS] [varchar] (100), [procprog] [varchar] (100), [PrtLabels] [varchar] (100), [rental] [varchar] (100), [rentcl] [varchar] (100), [TexDiesel] [varchar] (100), [Transport] [varchar] (100), [Use_Original] [varchar] (100));
";
            Log.Information($"ExecuteNonQueryAsync: \r\n{sql}");
            var comret1 = await _rawSql.ExecuteNonQueryAsync(sql);

            sql = $@"
INSERT INTO wksf_writecom ([1],[2],[3],[4],[5],[6],[7],[8],[9],[10],[11],[15],[20],[apply_to_inv],[dialup_company],[dialup_contact],[dialup_fax],[finance_cust],[finance_name],[got_contracts],[isQuote],[NewOrd],[OeMeter],[outofterr],[override_date],[Print_Part_Numbers],[Print_PartMfr_PL],[PrintPrices],[PrintSDS],[procprog],[PrtLabels],[rental],[rentcl],[TexDiesel],[Transport],[Use_Original])
                    VALUES('' ,'' ,'' ,'' ,'' ,'' ,'' ,'' ,'' ,''  ,''  ,''  ,''  ,''            ,''              ,''              ,''          ,''            ,''            ,''             ,''       ,''      ,''       ,''         ,''             ,''                  ,''                ,''           ,''        ,''        ,''         ,''      ,''      ,''         ,''         ,'')
UPDATE w
SET 
w.[1] = '0',
w.[2] = '{rechead.Branch__c}',
w.[3] = c.custpcl,
w.[4] = 'CRN',
w.[5] = '{rechead.EBS_Customer_ID__c}',
w.[6] = c.custrcl,
w.[7] = c.custlcl,
w.[8] = c.custecl,
w.[9] = c.taxcodes,
w.[10] = 'P',
w.[11] = c.custtaxbl,
w.[15] = 'S',
w.[20] = 'admin999',
w.[apply_to_inv] = '00000000',
w.[dialup_company] = c.custname,
w.[override_date] = '{DateTime.Now:MMddyy}',
w.[Print_Part_Numbers] = 'N',
w.[Print_PartMfr_PL] = 'Y',
w.[PrintPrices] = 'Y',
w.[PrtLabels] = 'N',
w.[rental] = 'YES',
w.[rentcl] = c.custrcl,
w.[procprog] = 'opsb001',
w.[NewOrd] = '{newKordnum:00000000}'
FROM wksf_writecom w
CROSS APPLY (SELECT TOP 1 * FROM custmast WHERE kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000') c
";
            Log.Information($"ExecuteNonQueryAsync: \r\n{sql}");
            var comret2 = await _rawSql.ExecuteNonQueryAsync(sql);

            sql = $@"
SET NOCOUNT ON
BEGIN TRY
	;with c as (
		select top 1 * from custmast where kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000'
	)
	INSERT INTO wk_oeh01_admin99999_88888 ([kdeleteflg],[oepordnum], [udlrcode],[kdoctype],[oetypeord],[ktermid] ,[ktermid1],[ktermid2],[ktermid3],[username],[kbranch]            ,[kcustnum],[custpcl],[custrcl],[custecl],[custlcl],[custphone],[custstdpo],[oecontact],[custsnum],[taxcodes],[kmake],[kmodel],[kserialnum],[kequipnum],[oedept],[oeslsrep] ,[custptrm],[oeptype],[custdisad1],[custdisad2],[custdisad3],[custshpvia],[custtcstk],[oeshipname],[oeshipadd],[oeshipad01],[oeshipad02],[oeshipcity],[oeshipstat],[oeshipzip],[oelostsale],[oelostreas],[oecomplete],[oeprtpick],[oeprcpick],[oejobnum],[program],[kordnum]   ,[oedate]                                          ,[advbill],[oeonrent]                                        ,[action],[oelastbill],[oenextbill]                                                  ,[date1]                ,[ktermidlk],[ktermidlk1],[dummy1],[dummy101],[dummy102],[dummy103],[dummy104],[dummy105],[dummy106],[dummy107],[dummy108],[dummy109],[dummy110],[dummy111],[dummy112],[dummy113],[dummy114],[dummy115],[dummy116],[dummy117],[dummy118],[dummy119],[oecusteqid],[arinvno],[oetaxex]  ,[oetrancode],[artotal],[artotal01],[artotal02],[artotal03],[artotal04],[artotal05],[artaxamt],[artaxamt01],[artaxamt02],[artaxamt03],[artaxamt04],[artaxamt05],[recnum],[crcardco],[crcardno],[ccexpdate]        ,[ccauth],[drlicst],[drlicno],[vehlicst],[vehlicno],[exretdt]                                                     ,[rentstart])
	SELECT                                 'N'   ,{newKordnum},'100'     ,'O'       ,'S'        ,'admin999','YES'     ,''        ,c.kcustnum,'Admin'   ,'{rechead.Branch__c}',c.kcustnum,c.custpcl,c.custrcl,c.custecl,c.custlcl,c.custphone,''         ,''         ,c.custsnum,c.taxcodes,''     ,''      ,''          ,''         ,''      ,'{rechead.Rep_Number__c}',c.custptrm,'O'      ,c.custdisad1,''          ,''          ,''          ,'H'        ,c.custname  ,c.custadd  ,''          ,''          ,c.custcity  ,c.custstate ,c.custzip  ,'N'         ,''          ,'E'         ,'N'        ,'D'        ,''        ,'OPSS001',{newKordnum},'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','Y'      ,'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','2'     ,NULL        ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.CreatedDate}','admin999' ,'CRN'       ,''      ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,'B'       ,''          ,0        ,c.custtaxbl,''          ,0        ,0          ,0          ,0          ,0          ,0          ,0         ,0           ,0           ,0           ,0           ,0           ,0       ,''        ,''        ,'20260101 00:00:00',''      ,''       ,''       ,''        ,''        ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}'
	FROM c

	;with c as (
		select top 1 * from custmast where kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000'
	)
	INSERT INTO oehead (kdeleteflg,udlrcode,kdoctype,oetypeord,ktermid   ,username,kbranch              ,kcustnum  ,custpcl  ,custrcl  ,custecl  ,custlcl  ,custphone  ,custstdpo,oecontact,custsnum  ,taxcodes  ,kmake,kmodel,kserialnum,kequipnum,oedept,oeslsrep   ,custptrm  ,oeptype,custdisad1  ,custdisad2,custdisad3,custshpvia,custtcstk,oeshipname,oeshipadd,oeshipad01,oeshipad02,oeshipcity,oeshipstat ,oeshipzip,oelostsale,oelostreas,oecomplete,oeprtpick,oeprcpick,oejobnum,program  ,kordnum     ,oedate                                            ,advbill,oeonrent                                          ,action,oelastbill,oenextbill                                                    ,date1                  ,oecusteqid,arinvno,oetaxex    ,oetrancode,artotal ,artotal01,artotal02,artotal03,artotal04,artotal05,artaxamt,artaxamt01,artaxamt02,artaxamt03,artaxamt04,artaxamt05,kworkorder,kswoseg,crcardco,crcardno,ccexpdate            ,drlicst,drlicno,vehlicst,vehlicno,exretdt                                                       ,rentstart)
	SELECT              'N'       ,'100'   ,'O'     ,'S'      ,'admin999','Admin' ,'{rechead.Branch__c}',c.kcustnum,c.custpcl,c.custrcl,c.custecl,c.custlcl,c.custphone,''       ,''       ,c.custsnum,c.taxcodes,''   ,''    ,''        ,''       ,''    ,'{rechead.Rep_Number__c}',c.custptrm,'O'    ,c.custdisad1,''        ,''        ,''        ,'H'      ,c.custname,c.custadd,''        ,''        ,c.custcity,c.custstate,c.custzip,'N'       ,''        ,'B'       ,'N'      ,'P'      ,''      ,'OPSS001',{newKordnum},'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','Y'    ,'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','2'   ,NULL      ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.CreatedDate}',''        ,0      ,c.custtaxbl,''        ,0.000000,0.000000 ,0.000000 ,0.000000 ,0.000000 ,0.000000 ,0.000000,0.000000  ,0.000000  ,0.000000  ,0.000000  ,0.000000  ,''        ,0   ,''      ,''      ,'2026-01-01 00:00:00',''     ,''     ,''      ,''      ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}'
	FROM c

    IF OBJECT_ID('tempdb..#pricing') IS NOT NULL DROP TABLE #pricing;
    CREATE TABLE #pricing ([kdeleteflg] varchar(1), [date1] datetime, [kbranch] varchar(3), [eqpigrp] varchar(3), [kmodel] varchar(12), [udesc] varchar(40), [uprcpbase] varchar(1), [uprcpbas01] varchar(1), [uprcppct] real, [uamount] float, [uamount01] float, [uamount02] float, [uamount03] float, [uamount04] float, [uamount05] float, [uamount06] float, [eqpraexcnt] smallint, [eqpraexc01] smallint, [eqpraexc02] smallint, [eqpraexc03] smallint, [eqpraexc04] smallint, [eqpraexc05] smallint, [usummary] varchar(1), [utaxexcpt] varchar(1), [oetrancode] varchar(2), [glco1] varchar(3), [glacct1] varchar(6), [glbr1] varchar(3), [gldpt1] varchar(3), [glco2] varchar(3), [glacct2] varchar(6), [glbr2] varchar(3), [gldpt2] varchar(3), [glco3] varchar(3), [glacct3] varchar(6), [glbr3] varchar(3), [gldpt3] varchar(3), [glco4] varchar(3), [glacct4] varchar(6), [glbr4] varchar(3), [gldpt4] varchar(3), [glco5] varchar(3), [glacct5] varchar(6), [glbr5] varchar(3), [gldpt5] varchar(3), [glco6] varchar(3), [glacct6] varchar(6), [glbr6] varchar(3), [gldpt6] varchar(3), [uprrprod] varchar(5), [uprrdesc2] varchar(40), [uprrdesc3] varchar(40), [uprrconpro] varchar(1), [uprrfrdte] datetime, [uprrtodte] datetime, [uprrcondes] varchar(40), [uprroper] real, [uprraddday] real, [uprraddwk] real, [uprraddmo] real, [uprrlngtrm] float, [uprrtravel] real, [uprrunaday] smallint, [uprrunawk] smallint, [uprrunamo] smallint, [uprpromrt] real, [uprpromr01] real, [uprpromr02] real, [uprpromr03] real, [uprpromr04] real, [uprpromr05] real, [uprpromr06] real, [uprpromr07] real, [uprpromr08] real, [uprpromr09] real, [uprpromhrs] smallint, [uprpromh01] smallint, [uprpromh02] smallint, [uprpromh03] smallint, [uprpromh04] smallint, [uprpromh05] smallint, [uprpromot] real, [uprpromo01] real, [uprpromo02] real, [uprprobase] smallint, [uprproba01] smallint)

{sqlProducts}


	EXEC msdb.dbo.startjob @job = 'SF_MOBILE653_Reservation'
	
	SELECT 'done'
END TRY
BEGIN CATCH
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE(), @ErrorSeverity INT = ERROR_SEVERITY(), @ErrorState INT = ERROR_STATE()
    RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
    SELECT @ErrorMessage AS ErrorMessage
END CATCH
SET NOCOUNT OFF
";
            Log.Information($"ExecuteScalarAsync (main qry): \r\n{sql}");
            var ret = await _rawSql.ExecuteScalarAsync<string>(sql);
            if (ret == "done")
            {
                Log.Information($"Successfully created order {newKordnum} for Quote {chrec.SalesforceRecordId}");
                await _storageService.MarkAsProcessedAsync(chrec.Id, error: null);
                return true;
            }
            else
            {
                Log.Error($"ExecuteScalarAsync (main qry) returned ErrorMessage: {ret}");
                await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"ExecuteScalarAsync (main qry) returned ErrorMessage: {ret}");
                return false;
            }

            //                var relatedRows = await _rawSql.ExecuteReaderAsync(@$"
            //SELECT [sfQuote_id], [Id], [OwnerId], [IsDeleted], [Name], [LastModifiedDate], [LastModifiedById], [OpportunityId], [Pricebook2Id], [ContactId], [QuoteNumber], [Status], [Description], [Subtotal], [TotalPrice], [LineItemCount], [BillingName], [ShippingName], [AccountId], [Discount], [GrandTotal], [Quote_Date__c], [Date__c], [EBS_Customer_ID__c], [Delivery_Date__c], [Term_Length_In_Days__c], [Rep_Number__c], [Branch__c], [Invoice__c]
            //from sfQuote 
            //where Id = '{recIdToProcess}'
            //");
            //                foreach (var row in relatedRows)
            //                {
            //                    Log.Information($"Related order: {row["OrderId"]} | Status: {row["Status"]} | Total: {row["Total"]}");
            //                }

            // ExecuteQuery<T> — typed result set mapped to a model class
            // var orders = await _rawSql.ExecuteQueryAsync<OrderSummary>(
            //     $"SELECT OrderId, CustomerName, Total FROM Orders WHERE QuoteId = '{record.SalesforceRecordId}'");

            // ExecuteReaderCallbackAsync — row-by-row for large result sets
            // await _rawSql.ExecuteReaderCallbackAsync(
            //     $"SELECT OrderId, Total FROM Orders WHERE QuoteId = '{record.SalesforceRecordId}'",
            //     async row => { await ProcessOrderAsync((int)row["OrderId"]!); });

            // ExecuteStoredProcedureNonQuery — stored procedures still use SqlParameter
            // await _rawSql.ExecuteStoredProcedureNonQueryAsync(
            //     "dbo.usp_ProcessQuote",
            //     new SqlParameter("@QuoteId", record.SalesforceRecordId));
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"OnQuoteChanged failed for Record {chrec.SalesforceRecordId}");
            await _storageService.MarkAsProcessedAsync(chrec.Id, error: $"OnQuoteChanged failed for Record {chrec.SalesforceRecordId}");
            return true;
        }
    }

    public async Task OnQuoteChangedAsync(ChangeEventRecord changeRecord)
    {
        try
        {
            string sql = "";
            string kequipnum = "";

            var results = await QueryAsync(
                $@"SELECT Id, OwnerId, IsDeleted, Name, CreatedDate, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp, LastViewedDate, LastReferencedDate, OpportunityId, Pricebook2Id, ContactId, QuoteNumber, IsSyncing, ShippingHandling, Tax, Status, ExpirationDate, Description, Subtotal, TotalPrice, LineItemCount, BillingStreet, BillingCity, BillingState, BillingPostalCode, BillingCountry, BillingLatitude, BillingLongitude, BillingGeocodeAccuracy, ShippingStreet, ShippingCity, ShippingState, ShippingPostalCode, ShippingCountry, ShippingLatitude, ShippingLongitude, ShippingGeocodeAccuracy, QuoteToStreet, QuoteToCity, QuoteToState, QuoteToPostalCode, QuoteToCountry, QuoteToLatitude, QuoteToLongitude, QuoteToGeocodeAccuracy, AdditionalStreet, AdditionalCity, AdditionalState, AdditionalPostalCode, AdditionalCountry, AdditionalLatitude, AdditionalLongitude, AdditionalGeocodeAccuracy, BillingName, ShippingName, QuoteToName, AdditionalName, Email, Phone, Fax, ContractId, AccountId, Discount, GrandTotal, CanCreateQuoteLineItems, Quote_Date__c, Date__c, EBS_Customer_ID__c, Delivery_Date__c, Term_Length_In_Days__c, Rep_Number__c, Branch__c, Invoice__c
                   FROM Quote
                   WHERE Id = '{changeRecord.SalesforceRecordId}'
                   ALL ROWS");

            if (results.Count == 0)
            {
                Log.Warning($"OnQuoteChanged: Quote {changeRecord.SalesforceRecordId} not found in Salesforce");
                return;
            }

            var quote = results[0];
            Log.Information($"OnQuoteChanged: Quote {changeRecord.SalesforceRecordId} | Status: {quote["Status"]} | Total: {quote["TotalPrice"]}");

            sfQuote sfo;
            try
            {
                sfo = quote.ToObject<sfQuote>()!;
                await _storageService.SaveQuote(sfo);
                Log.Information($"Saved {sfo.GetType().Name} {changeRecord.SalesforceRecordId} to database");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deserialize sfQuote {changeRecord.SalesforceRecordId}");
            }

            //RYAN - compare server line items to local in case anything has changed and we missed the event
            //   ONLY if ChangeType is UPDATE... and only if ChangeFields contains "STATUS"
            if (changeRecord.ChangeType == "UPDATE" && (changeRecord.ChangedFields ?? "").Contains("Status"))
            {

                try
                {
                    List<JObject> qliresults = [];
                    qliresults = await QueryAsync(
        $@"SELECT Id, IsDeleted, LineNumber, CreatedDate, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp, LastViewedDate, LastReferencedDate, QuoteId, PricebookEntryId, OpportunityLineItemId, Quantity, UnitPrice, Discount, Description, ServiceDate, Product2Id, SortOrder, ListPrice, Subtotal, TotalPrice, ProductID__c, SerialNumber__c, UID__c, Inventory__c
                   FROM QuoteLineItem
                   WHERE QuoteId = '{changeRecord.SalesforceRecordId}'
                   ALL ROWS");

                    if (qliresults.Count == 0)
                    {
                        Log.Warning($"OnQuoteChanged: LineItems not found for quote {changeRecord.SalesforceRecordId}");
                        return;
                    }

                    List<sfQuoteLineItem> items = [];
                    foreach (var lineItem in qliresults)
                    {
                        sfQuoteLineItem sfo2;
                        try
                        {
                            sfo2 = lineItem.ToObject<sfQuoteLineItem>();
                            items.Add(sfo2);
                            //Log.Information($"Saved {sfo.GetType().Name} {changeRecord.SalesforceRecordId} to database");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"Failed to deserialize sfQuoteLineItem {changeRecord.SalesforceRecordId}");
                            return;
                        }
                    }
                    await _storageService.SaveQuoteItems(items, changeRecord.SalesforceRecordId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to retrieve quote line items for sfQuote {changeRecord.SalesforceRecordId}");
                }
            }

            bool bKeepProcessing = true;
            while (bKeepProcessing)
            {
                try
                {
                    ChangeEventRecord? chrec;
                    chrec = await _storageService.GetNextUnprocessedChangeAsync();
                    if (chrec != null)
                    {
                        Log.Information($"Quote {chrec.Name} ({chrec.SalesforceRecordId}) — ready to process.");
                        bKeepProcessing = await HandleUnprocessedQuoteAsync(chrec);
                        if (bKeepProcessing) { Log.Information($"HandleUnprocessedQuoteAsync returned true. Keep processing until false."); }
                    }
                    else
                    {
                        Log.Information($"No unprocessed quote records found.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Critical error retrieving unprocessed Quote change event: {ex.Message}\r\n{ex.StackTrace}");
                    return;
                }
            }

            /*
            if (changeRecord.EntityType == "Quote" && changeRecord.Status == "Approved")
            {
                await _rawSql.ExecuteNonQueryAsync(@$"
                IF EXISTS (SELECT * FROM sfProcessingQueue WHERE QuoteId = '{changeRecord.SalesforceRecordId}')
                    UPDATE sfProcessingQueue SET Status = 'Pending', ModifiedAt = '{DateTime.Now:yyyy-MM-dd HH:mm:ss}' WHERE QuoteId = '{changeRecord.SalesforceRecordId}'
                ELSE
                    INSERT INTO sfProcessingQueue (QuoteId, Status, ModifiedAt) VALUES ('{changeRecord.SalesforceRecordId}', 'Pending', '{DateTime.Now:yyyy-MM-dd HH:mm:ss}')
                ");
            }
            */

            /*
            var recIdToProcess = await _rawSql.ExecuteScalarAsync<int>(@$"
SELECT TOP 1 Id
FROM ChangeEvents
WHERE EntityType = 'Quote' AND Status = 'Approved' AND IsProcessed = 0 AND ProcessingError IS NULL
ORDER BY Id DESC
");
            if (recIdToProcess > 0)
            {
                Log.Information($"Quote {changeRecord.Name} — ready to process. sfID: {changeRecord.SalesforceRecordId}");
            }
            else
            {
                Log.Information($"No unprocessed records found for Quote {changeRecord.SalesforceRecordId} — skipping");
                return;
            }

            var rechead = await _storageService.GetQuoteByIdAsync(sfo.Id);
            if (rechead != null)
            {

            }
            else
            {
                // update change event record to indicate processing error
                Log.Warning($"Quote {changeRecord.SalesforceRecordId} not found in local database after saving");
                return;
            }

            var recdet = await _storageService.GetQuoteLinesByIdAsync(sfo.Id);
            if (recdet.Count > 0)
            {

            }
            else
            {
                // update change event record to indicate processing error
                Log.Warning($"Quote line items for quote {changeRecord.SalesforceRecordId} not found");
                return;
            }

            List<sfProduct2> products2 = new List<sfProduct2>();
            foreach (var line in recdet)
            {
                Log.Information($"Quote {changeRecord.SalesforceRecordId} line item: {line.Product2Id} | Qty: {line.Quantity} | Price: {line.UnitPrice}");
                var pr = await _storageService.GetProductByIdAsync(line.Product2Id);
                if (pr != null)
                {
                    products2.Add(pr);
                    string? keq = await _rawSql.ExecuteScalarAsync<string>(@$"
select top 1 COALESCE(e.kequipnum, e2.kequipnum) as kequipnum
from sfProduct2 p 
inner join utgrpprod u on p.Product_Group__c = u.eqpigrp and p.Product_Code__c = u.gmmottype
cross apply (
select * 
from equip 
where eqpigrp = u.eqpigrp 
    and gmmottype = u.gmmottype 
    and eqpstatus = 'AV'
    and eqpphybr = '{rechead.Branch__c}'
) e
cross apply (
select * 
from equip 
where eqpigrp = u.eqpigrp 
    and gmmottype = u.gmmottype 
    and eqpstatus = 'AV'
    and eqpphybr <> '{rechead.Branch__c}'
) e2
where p.Id = '{pr.Id}'
order by e.eqprecdt
");
                    if (keq != null)
                    {
                        kequipnum = keq;
                        Log.Information($"Found available equipment {kequipnum} for quote {changeRecord.SalesforceRecordId}");
                    }
                    else
                    {
                        // update change event record to indicate processing error
                        Log.Warning($"No available equipment found for quote {changeRecord.SalesforceRecordId}");
                        return;
                    }
                }
                else
                {
                    // update change event record to indicate processing error
                    Log.Warning($"Product {line.Product2Id} not found in local database");
                    return;
                }
            }


            //scalar query for kordnum
            var newKordnum = await _rawSql.ExecuteScalarAsync<int>(@$"
DECLARE @neword Int = 0
BEGIN TRY
BEGIN TRANSACTION
SELECT TOP 1 @neword = kordnum + 1 FROM utbr WHERE kbranch = '{rechead.Branch__c}' ORDER BY kbranch,utbr_id
UPDATE utbr SET kordnum = @neword WHERE kbranch = '{rechead.Branch__c}'
COMMIT TRANSACTION
SELECT @neword
END TRY
BEGIN CATCH
ROLLBACK TRANSACTION
SELECT @neword
END CATCH
");
            if (newKordnum > 0)
            {
                Log.Information($"Retrieved new kordnum: {newKordnum}");
            }
            else
            {
                // update change event record to indicate processing error
                Log.Warning($"Failed to retrieve new kordnum");
                return;
            }

            sql = @$"
drop table if exists wk_oeh01_admin99999_88888;
drop table if exists wk_oed01_admin99999_88888;
drop table if exists wk_eqm01_admin99999_88888;
CREATE TABLE [dbo].[wk_oeh01_admin99999_88888] ( [wk_oeh01_admin99999_88888_id] [int] IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_wk_oeh01_admin99999_88888] PRIMARY KEY (wk_oeh01_admin99999_88888_id), [kdeleteflg] [varchar] (1) NULL, [oepordnum] [int] NULL, [udlrcode] [varchar] (3) NULL, [kdoctype] [varchar] (1) NULL, [oetypeord] [varchar] (1) NULL, [ktermid] [varchar] (8) NULL, [ktermid1] [varchar] (8) NULL, [ktermid2] [varchar] (8) NULL, [ktermid3] [varchar] (8) NULL, [username] [varchar] (8) NULL, [kbranch] [varchar] (3) NULL, [kcustnum] [varchar] (8) NULL, [custpcl] [varchar] (2) NULL, [custrcl] [varchar] (2) NULL, [custecl] [varchar] (2) NULL, [custlcl] [varchar] (2) NULL, [custphone] [varchar] (16) NULL, [custstdpo] [varchar] (30) NULL, [oecontact] [varchar] (30) NULL, [custsnum] [varchar] (3) NULL, [taxcodes] [varchar] (10) NULL, [kmake] [varchar] (3) NULL, [kmodel] [varchar] (12) NULL, [kserialnum] [varchar] (15) NULL, [kequipnum] [varchar] (12) NULL, [oedept] [varchar] (3) NULL, [oeslsrep] [varchar] (3) NULL, [custptrm] [smallint] NULL, [oeptype] [varchar] (1) NULL, [custdisad1] [varchar] (3) NULL, [custdisad2] [varchar] (3) NULL, [custdisad3] [varchar] (3) NULL, [custshpvia] [varchar] (20) NULL, [custtcstk] [varchar] (2) NULL, [oeshipname] [varchar] (35) NULL, [oeshipadd] [varchar] (35) NULL, [oeshipad01] [varchar] (35) NULL, [oeshipad02] [varchar] (35) NULL, [oeshipcity] [varchar] (25) NULL, [oeshipstat] [varchar] (2) NULL, [oeshipzip] [varchar] (10) NULL, [oelostsale] [varchar] (1) NULL, [oelostreas] [varchar] (1) NULL, [oecomplete] [varchar] (1) NULL, [oeprtpick] [varchar] (1) NULL, [oeprcpick] [varchar] (1) NULL, [oejobnum] [varchar] (20) NULL, [program] [varchar] (10) NULL, [kordnum] [int] NULL, [oedate] [datetime] NULL, [advbill] [varchar] (1) NULL, [oeonrent] [datetime] NULL, [action] [varchar] (1) NULL, [oelastbill] [datetime] NULL, [oenextbill] [datetime] NULL, [date1] [datetime] NULL, [ktermidlk] [varchar] (8) NULL, [ktermidlk1] [varchar] (8) NULL, [dummy1] [varchar] (1) NULL, [dummy101] [varchar] (1) NULL, [dummy102] [varchar] (1) NULL, [dummy103] [varchar] (1) NULL, [dummy104] [varchar] (1) NULL, [dummy105] [varchar] (1) NULL, [dummy106] [varchar] (1) NULL, [dummy107] [varchar] (1) NULL, [dummy108] [varchar] (1) NULL, [dummy109] [varchar] (1) NULL, [dummy110] [varchar] (1) NULL, [dummy111] [varchar] (1) NULL, [dummy112] [varchar] (1) NULL, [dummy113] [varchar] (1) NULL, [dummy114] [varchar] (1) NULL, [dummy115] [varchar] (1) NULL, [dummy116] [varchar] (1) NULL, [dummy117] [varchar] (1) NULL, [dummy118] [varchar] (1) NULL, [dummy119] [varchar] (1) NULL, [oecusteqid] [varchar] (12) NULL, [arinvno] [int] NULL, [oetaxex] [varchar] (1) NULL, [oetrancode] [varchar] (2) NULL, [artotal] [float] NULL, [artotal01] [float] NULL, [artotal02] [float] NULL, [artotal03] [float] NULL, [artotal04] [float] NULL, [artotal05] [float] NULL, [artaxamt] [float] NULL, [artaxamt01] [float] NULL, [artaxamt02] [float] NULL, [artaxamt03] [float] NULL, [artaxamt04] [float] NULL, [artaxamt05] [float] NULL, [recnum] [int] NULL, [crcardco] [varchar] (5) NULL, [crcardno] [varchar] (20) NULL, [ccexpdate] [datetime] NULL, [ccauth] [varchar] (10) NULL, [drlicst] [varchar] (2) NULL, [drlicno] [varchar] (15) NULL, [vehlicst] [varchar] (2) NULL, [vehlicno] [varchar] (10) NULL, [exretdt] [datetime] NULL, [rentstart] [datetime] NULL CONSTRAINT wk_oeh01_admin99999_88888_id_0 UNIQUE NONCLUSTERED(oepordnum) ) 
CREATE TABLE [dbo].[wk_oed01_admin99999_88888] ( [wk_oed01_admin99999_88888_id] [int] IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_wk_oed01_admin99999_88888] PRIMARY KEY (wk_oed01_admin99999_88888_id), [kdeleteflg] [varchar] (1) NULL, [oeptype] [varchar] (1) NULL, [oepordnum] [int] NULL, [ktermid] [varchar] (8) NULL, [kbranch] [varchar] (3) NULL, [key_3_a1] [varchar] (3) NULL, [kmfg] [varchar] (3) NULL, [kpart] [varchar] (20) NULL, [custpcl] [varchar] (2) NULL, [custrcl] [varchar] (2) NULL, [custecl] [varchar] (2) NULL, [custlcl] [varchar] (2) NULL, [oepqtyord] [real] NULL, [oeqtyship] [real] NULL, [pmdesc] [varchar] (40) NULL, [iclocmain] [varchar] (8) COLLATE SQL_Latin1_General_Cp437_Bin NULL, [key_8_a1] [varchar] (8) NULL, [key_8_a101] [varchar] (8) NULL, [key_8_a102] [varchar] (8) NULL, [icstatus] [varchar] (2) NULL, [oepsell] [float] NULL, [oetrancode] [varchar] (2) NULL, [oetaxex] [varchar] (1) NULL, [iccost] [real] NULL, [pmcommod] [varchar] (4) NULL, [pmum] [varchar] (2) NULL, [kordnum] [int] NULL, [oefrexecpt] [varchar] (1) NULL, [oenetdtl] [varchar] (1) NULL, [oefactor] [varchar] (1) NULL, [icdlrcl] [varchar] (3) NULL, [iclocsec] [varchar] (8) NULL, [icqtyonord] [int] NULL, [pmret] [varchar] (1) NULL, [eqpmeter] [smallint] NULL, [eqpmeter01] [smallint] NULL, [pmrep] [varchar] (1) NULL, [pmmcl] [varchar] (3) NULL, [pmpriceid] [smallint] NULL, [artotal] [float] NULL, [artotal01] [float] NULL, [artotal02] [float] NULL, [artotal03] [float] NULL, [artotal04] [float] NULL, [artotal05] [float] NULL, [artotal06] [float] NULL, [artotal07] [float] NULL, [artotal08] [float] NULL, [artotal09] [float] NULL, [artotal10] [float] NULL, [artotal11] [float] NULL, [oeitemnum] [smallint] NULL, [icbrchg] [varchar] (3) NULL, [dummy] [varchar] (1) NULL, [dummy01] [varchar] (1) NULL, [dummy02] [varchar] (1) NULL, [dummy03] [varchar] (1) NULL, [dummy04] [varchar] (1) NULL, [dummy05] [varchar] (1) NULL, [dummy06] [varchar] (1) NULL, [dummy07] [varchar] (1) NULL, [dummy08] [varchar] (1) NULL, [dummy09] [varchar] (1) NULL, [oerentot] [float] NULL, [oerenthr] [float] NULL, [oerentwk] [float] NULL, [oerentday] [float] NULL, [oerentmnth] [float] NULL, [kmodel] [varchar] (12) NULL, [date1] [datetime] NULL, [apdateons] [datetime] NULL, [eqpigrp] [varchar] (3) NULL, [oecomplete] [varchar] (1) NULL, [action] [varchar] (1) NULL, [shift] [varchar] (1) NULL, [key_4_a1] [varchar] (4) NULL, [uprpromrt] [real] NULL, [uprpromr01] [real] NULL, [uprpromr02] [real] NULL, [uprpromr03] [real] NULL, [uprpromr04] [real] NULL, [uprpromr05] [real] NULL, [uprpromr06] [real] NULL, [uprpromr07] [real] NULL, [uprpromr08] [real] NULL, [uprpromr09] [real] NULL, [wonotes] [varchar] (70) NULL, [oeshipclas] [varchar] (20) NULL, [glco1] [varchar] (3) NULL, [glacct1] [varchar] (6) NULL, [glbr1] [varchar] (3) NULL, [gldpt1] [varchar] (3) NULL, [recnum] [int] NULL, [recnum01] [int] NULL, [recnum02] [int] NULL, [recnum03] [int] NULL, [recnum04] [int] NULL, [recnum05] [int] NULL, [recnum06] [int] NULL, [recnum07] [int] NULL, [recnum08] [int] NULL, [recnum09] [int] NULL, [key_10_a1] [varchar] (10) NULL, [key_10_a01] [varchar] (10) NULL, [key_10_a02] [varchar] (10) NULL, [key_10_a03] [varchar] (10) NULL, [key_10_a04] [varchar] (10) NULL, [key_10_a05] [varchar] (10) NULL, [key_10_a06] [varchar] (10) NULL, [key_10_a07] [varchar] (10) NULL, [key_10_a08] [varchar] (10) NULL, [key_10_a09] [varchar] (10) NULL, [key_2_a1] [varchar] (2) NULL, [key_2_a101] [varchar] (2) NULL, [key_2_a102] [varchar] (2) NULL, [key_2_a103] [varchar] (2) NULL, [key_2_a104] [varchar] (2) NULL, [key_2_a105] [varchar] (2) NULL, [key_2_a106] [varchar] (2) NULL, [key_2_a107] [varchar] (2) NULL, [key_2_a108] [varchar] (2) NULL, [key_2_a109] [varchar] (2) NULL, [amtlast] [float] NULL, [amtlast01] [float] NULL, [amtlast02] [float] NULL, [amtlast03] [float] NULL, [amtlast04] [float] NULL, [amtlast05] [float] NULL, [amtlast06] [float] NULL, [amtlast07] [float] NULL, [amtlast08] [float] NULL, [amtlast09] [float] NULL, [curractdt] [datetime] NULL, [curractd01] [datetime] NULL, [curractd02] [datetime] NULL, [curractd03] [datetime] NULL, [curractd04] [datetime] NULL, [curractd05] [datetime] NULL, [curractd06] [datetime] NULL, [curractd07] [datetime] NULL, [curractd08] [datetime] NULL, [curractd09] [datetime] NULL, [pmset] [smallint] NULL CONSTRAINT wk_oed01_admin99999_88888_id_0 UNIQUE NONCLUSTERED(oeitemnum,icbrchg) ) 
CREATE TABLE [dbo].[wk_eqm01_admin99999_88888] ( [wk_eqm01_admin99999_88888_id] [int] IDENTITY (1, 1) NOT NULL CONSTRAINT [PK_wk_eqm01_admin99999_88888] PRIMARY KEY (wk_eqm01_admin99999_88888_id), [kequipnum] [varchar] (12) NULL, [kmfg] [varchar] (3) NULL, [kmodel] [varchar] (12) NULL, [kserialnum] [varchar] (15) NULL, [eqpigrp] [varchar] (3) NULL, [eqpsize] [varchar] (4) NULL, [eqptype] [varchar] (2) NULL, [eqpstatus] [varchar] (2) NULL, [eqpstatdt] [datetime] NULL, [eqpyrmfg] [varchar] (2) NULL, [eqpmeter] [smallint] NULL, [eqpphybr] [varchar] (3) NULL, [eqpdesc] [varchar] (40) NULL, [eqprntcust] [varchar] (8) NULL, [eqprano] [int] NULL, [eqponrdt] [datetime] NULL, [eqpoffrdt] [datetime] NULL, [eqpretno] [varchar] (10) NULL, [eqpsldcust] [varchar] (8) NULL, [eqpsldinv] [varchar] (8) NULL, [eqpsldamt] [float] NULL, [eqpslddate] [datetime] NULL, [eqpdimen] [varchar] (20) NULL, [eqpwarperd] [smallint] NULL, [eqpwarpts] [real] NULL, [eqpwarlab] [real] NULL, [eqpwaroth] [real] NULL, [eqpwarodt] [datetime] NULL, [eqprnthrly] [real] NULL, [eqpsellprc] [float] NULL, [eqpinsvalu] [real] NULL, [eqprntday] [real] NULL, [eqprntweek] [real] NULL, [eqprntmnth] [real] NULL, [eqprntover] [real] NULL, [eqporigcst] [float] NULL, [eqpfrtin] [real] NULL, [eqpinvrecd] [varchar] (12) NULL, [eqprecdt] [datetime] NULL, [eqpporec] [varchar] (30) NULL, [eqporddt] [datetime] NULL, [eqppurvend] [varchar] (8) NULL, [eqpfinvend] [varchar] (8) NULL, [eqpfinote] [varchar] (12) NULL, [eqpfinamt] [float] NULL, [eqpfinrate] [real] NULL, [eqpfinterm] [smallint] NULL, [eqpfindue] [datetime] NULL, [eqprepexp] [real] NULL, [eqprepcap] [real] NULL, [eqpwrtdn] [real] NULL, [eqptrnsfr] [real] NULL, [eqpcos] [float] NULL, [eqpinc] [float] NULL, [eqpyrrep] [real] NULL, [eqpyrinc] [real] NULL, [eqpyrdepr] [float] NULL, [eqpyrint] [real] NULL, [glco] [varchar] (3) NULL, [glacct] [varchar] (6) NULL, [glbr] [varchar] (3) NULL, [gldpt] [varchar] (3) NULL, [eqpfaexplf] [smallint] NULL, [eqpfaremlf] [smallint] NULL, [eqpfamethd] [varchar] (1) NULL, [eqpfarate] [real] NULL, [eqpfaamt] [real] NULL, [eqpfasalvg] [real] NULL, [eqpraexcnt] [smallint] NULL, [date1] [datetime] NULL, [action] [varchar] (1) NULL, [kdeleteflg] [varchar] (1) NULL, [dummy] [varchar] (1) NULL, [dummy01] [varchar] (1) NULL, [dummy02] [varchar] (1) NULL, [icqty] [int] NULL, [program] [varchar] (10) NULL, [username] [varchar] (8) NULL CONSTRAINT wk_eqm01_admin99999_88888_id_0 UNIQUE NONCLUSTERED(kdeleteflg,kequipnum) ) 
";
            Log.Information($"ExecuteNonQueryAsync: \r\n{sql}");
            var rows = await _rawSql.ExecuteNonQueryAsync(sql);
            sql = $@"
IF OBJECT_ID('wksf_writecom','U') IS NOT NULL DROP TABLE [dbo].[wksf_writecom];
CREATE TABLE [dbo].[wksf_writecom] ([1] [varchar] (100), [2] [varchar] (100), [3] [varchar] (100), [4] [varchar] (100), [5] [varchar] (100), [6] [varchar] (100), [7] [varchar] (100), [8] [varchar] (100), [9] [varchar] (100), [10] [varchar] (100), [11] [varchar] (100), [15] [varchar] (100), [20] [varchar] (100), [apply_to_inv] [varchar] (100), [dialup_company] [varchar] (100), [dialup_contact] [varchar] (100), [dialup_fax] [varchar] (100), [finance_cust] [varchar] (100), [finance_name] [varchar] (100), [got_contracts] [varchar] (100), [isQuote] [varchar] (100), [NewOrd] [varchar] (100), [OeMeter] [varchar] (100), [outofterr] [varchar] (100), [override_date] [varchar] (100), [Print_Part_Numbers] [varchar] (100), [Print_PartMfr_PL] [varchar] (100), [PrintPrices] [varchar] (100), [PrintSDS] [varchar] (100), [procprog] [varchar] (100), [PrtLabels] [varchar] (100), [rental] [varchar] (100), [rentcl] [varchar] (100), [TexDiesel] [varchar] (100), [Transport] [varchar] (100), [Use_Original] [varchar] (100));
";
            Log.Information($"ExecuteNonQueryAsync: \r\n{sql}");
            var comret1 = await _rawSql.ExecuteNonQueryAsync(sql);

            sql = $@"
INSERT INTO wksf_writecom ([1],[2],[3],[4],[5],[6],[7],[8],[9],[10],[11],[15],[20],[apply_to_inv],[dialup_company],[dialup_contact],[dialup_fax],[finance_cust],[finance_name],[got_contracts],[isQuote],[NewOrd],[OeMeter],[outofterr],[override_date],[Print_Part_Numbers],[Print_PartMfr_PL],[PrintPrices],[PrintSDS],[procprog],[PrtLabels],[rental],[rentcl],[TexDiesel],[Transport],[Use_Original])
                VALUES('' ,'' ,'' ,'' ,'' ,'' ,'' ,'' ,'' ,''  ,''  ,''  ,''  ,''            ,''              ,''              ,''          ,''            ,''            ,''             ,''       ,''      ,''       ,''         ,''             ,''                  ,''                ,''           ,''        ,''        ,''         ,''      ,''      ,''         ,''         ,'')
UPDATE w
SET 
w.[1] = '0',
w.[2] = '{rechead.Branch__c}',
w.[3] = c.custpcl,
w.[4] = 'CRN',
w.[5] = '{rechead.EBS_Customer_ID__c}',
w.[6] = c.custrcl,
w.[7] = c.custlcl,
w.[8] = c.custecl,
w.[9] = c.taxcodes,
w.[10] = 'P',
w.[11] = c.custtaxbl,
w.[15] = 'S',
w.[20] = 'admin999',
w.[apply_to_inv] = '00000000',
w.[dialup_company] = c.custname,
w.[override_date] = '{DateTime.Now:MMddyy}',
w.[Print_Part_Numbers] = 'N',
w.[Print_PartMfr_PL] = 'Y',
w.[PrintPrices] = 'Y',
w.[PrtLabels] = 'N',
w.[rental] = 'YES',
w.[rentcl] = c.custrcl,
w.[procprog] = 'opsb001',
w.[NewOrd] = '{newKordnum:00000000}'
FROM wksf_writecom w
CROSS APPLY (SELECT TOP 1 * FROM custmast WHERE kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000') c
";
            Log.Information($"ExecuteNonQueryAsync: \r\n{sql}");
            var comret2 = await _rawSql.ExecuteNonQueryAsync(sql);

            sql = $@"
;with c as (
select top 1 * from custmast where kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000'
)
INSERT INTO wk_oeh01_admin99999_88888 ([kdeleteflg],[oepordnum], [udlrcode],[kdoctype],[oetypeord],[ktermid] ,[ktermid1],[ktermid2],[ktermid3],[username],[kbranch]            ,[kcustnum],[custpcl],[custrcl],[custecl],[custlcl],[custphone],[custstdpo],[oecontact],[custsnum],[taxcodes],[kmake],[kmodel],[kserialnum],[kequipnum],[oedept],[oeslsrep] ,[custptrm],[oeptype],[custdisad1],[custdisad2],[custdisad3],[custshpvia],[custtcstk],[oeshipname],[oeshipadd],[oeshipad01],[oeshipad02],[oeshipcity],[oeshipstat],[oeshipzip],[oelostsale],[oelostreas],[oecomplete],[oeprtpick],[oeprcpick],[oejobnum],[program],[kordnum]   ,[oedate]                                          ,[advbill],[oeonrent]                                        ,[action],[oelastbill],[oenextbill]                                                  ,[date1]                ,[ktermidlk],[ktermidlk1],[dummy1],[dummy101],[dummy102],[dummy103],[dummy104],[dummy105],[dummy106],[dummy107],[dummy108],[dummy109],[dummy110],[dummy111],[dummy112],[dummy113],[dummy114],[dummy115],[dummy116],[dummy117],[dummy118],[dummy119],[oecusteqid],[arinvno],[oetaxex]  ,[oetrancode],[artotal],[artotal01],[artotal02],[artotal03],[artotal04],[artotal05],[artaxamt],[artaxamt01],[artaxamt02],[artaxamt03],[artaxamt04],[artaxamt05],[recnum],[crcardco],[crcardno],[ccexpdate]        ,[ccauth],[drlicst],[drlicno],[vehlicst],[vehlicno],[exretdt]                                                     ,[rentstart])
SELECT                                 'N'   ,{newKordnum},'100'     ,'O'       ,'S'        ,'admin999','YES'     ,''        ,c.kcustnum,'Admin'   ,'{rechead.Branch__c}',c.kcustnum,c.custpcl,c.custrcl,c.custecl,c.custlcl,c.custphone,''         ,''         ,c.custsnum,c.taxcodes,''     ,''      ,''          ,''         ,''      ,c.custslsmn,c.custptrm,'O'      ,c.custdisad1,''          ,''          ,''          ,'H'        ,c.custname  ,c.custadd  ,''          ,''          ,c.custcity  ,c.custstate ,c.custzip  ,'N'         ,''          ,'E'         ,'N'        ,'D'        ,''        ,'OPSS001',{newKordnum},'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','Y'      ,'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','2'     ,NULL        ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.CreatedDate}','admin999' ,'CRN'       ,''      ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,''        ,'B'       ,''          ,0        ,c.custtaxbl,''          ,0        ,0          ,0          ,0          ,0          ,0          ,0         ,0           ,0           ,0           ,0           ,0           ,0       ,''        ,''        ,'20260101 00:00:00',''      ,''       ,''       ,''        ,''        ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}'
FROM c

;with c as (
select top 1 * from custmast where kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000'
)
INSERT INTO oehead (kdeleteflg,udlrcode,kdoctype,oetypeord,ktermid   ,username,kbranch              ,kcustnum  ,custpcl  ,custrcl  ,custecl  ,custlcl  ,custphone  ,custstdpo,oecontact,custsnum  ,taxcodes  ,kmake,kmodel,kserialnum,kequipnum,oedept,oeslsrep   ,custptrm  ,oeptype,custdisad1  ,custdisad2,custdisad3,custshpvia,custtcstk,oeshipname,oeshipadd,oeshipad01,oeshipad02,oeshipcity,oeshipstat ,oeshipzip,oelostsale,oelostreas,oecomplete,oeprtpick,oeprcpick,oejobnum,program  ,kordnum     ,oedate                                            ,advbill,oeonrent                                          ,action,oelastbill,oenextbill                                                    ,date1                  ,oecusteqid,arinvno,oetaxex    ,oetrancode,artotal ,artotal01,artotal02,artotal03,artotal04,artotal05,artaxamt,artaxamt01,artaxamt02,artaxamt03,artaxamt04,artaxamt05,kworkorder,kswoseg,crcardco,crcardno,ccexpdate            ,drlicst,drlicno,vehlicst,vehlicno,exretdt                                                       ,rentstart)
SELECT              'N'       ,'100'   ,'O'     ,'S'      ,'admin999','Admin' ,'{rechead.Branch__c}',c.kcustnum,c.custpcl,c.custrcl,c.custecl,c.custlcl,c.custphone,''       ,''       ,c.custsnum,c.taxcodes,''   ,''    ,''        ,''       ,''    ,c.custslsmn,c.custptrm,'O'    ,c.custdisad1,''        ,''        ,''        ,'H'      ,c.custname,c.custadd,''        ,''        ,c.custcity,c.custstate,c.custzip,'N'       ,''        ,'B'       ,'N'      ,'P'      ,''      ,'OPSS001',{newKordnum},'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','Y'    ,'{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','2'   ,NULL      ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.CreatedDate}',''        ,0      ,c.custtaxbl,''        ,0.000000,0.000000 ,0.000000 ,0.000000 ,0.000000 ,0.000000 ,0.000000,0.000000  ,0.000000  ,0.000000  ,0.000000  ,0.000000  ,''        ,-999   ,''      ,''      ,'2026-01-01 00:00:00',''     ,''     ,''      ,''      ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}'
FROM c


;with c as (
select top 1 * from custmast where kcustnum = '{rechead.EBS_Customer_ID__c}' and custsnum = '000'
)
INSERT INTO wk_oed01_admin99999_88888 ([kdeleteflg],[oeptype],[oepordnum],[ktermid] ,[kbranch],            [key_3_a1],[kmfg],[kpart]      ,[custpcl],[custrcl],[custecl],[custlcl],[oepqtyord],[oeqtyship],[pmdesc]                 ,[iclocmain],[key_8_a1],[key_8_a101],[key_8_a102],[icstatus],[oepsell],[oetrancode],[oetaxex],[iccost],[pmcommod],[pmum],[kordnum],   [oefrexecpt],[oenetdtl],[oefactor],[icdlrcl],[iclocsec],[icqtyonord],[pmret],[eqpmeter],[eqpmeter01],[pmrep],[pmmcl],[pmpriceid],[artotal],[artotal01],[artotal02],[artotal03],[artotal04],[artotal05],[artotal06],[artotal07],[artotal08],[artotal09],[artotal10],[artotal11],[oeitemnum],[icbrchg],[dummy],[dummy01],[dummy02],[dummy03],[dummy04],[dummy05],[dummy06],[dummy07],[dummy08],[dummy09],[oerentot],[oerenthr],[oerentwk],[oerentday],[oerentmnth],[kmodel],[date1]            ,[apdateons]        ,[eqpigrp],[oecomplete],[action],[shift],[key_4_a1],[uprpromrt],[uprpromr01],[uprpromr02],[uprpromr03],[uprpromr04],[uprpromr05],[uprpromr06],[uprpromr07],[uprpromr08],[uprpromr09],[wonotes],[oeshipclas],[glco1],[glacct1],[glbr1],[gldpt1],[recnum],[recnum01],[recnum02],[recnum03],[recnum04],[recnum05],[recnum06],[recnum07],[recnum08],[recnum09],[key_10_a1],[key_10_a01],[key_10_a02],[key_10_a03],[key_10_a04],[key_10_a05],[key_10_a06],[key_10_a07],[key_10_a08],[key_10_a09],[key_2_a1],[key_2_a101],[key_2_a102],[key_2_a103],[key_2_a104],[key_2_a105],[key_2_a106],[key_2_a107],[key_2_a108],[key_2_a109],[amtlast],[amtlast01],[amtlast02],[amtlast03],[amtlast04],[amtlast05],[amtlast06],[amtlast07],[amtlast08],[amtlast09],[curractdt],[curractd01],[curractd02],[curractd03],[curractd04],[curractd05],[curractd06],[curractd07],[curractd08],[curractd09],[pmset])
SELECT                                 'N'         ,'4'      ,1          ,'admin999','{rechead.Branch__c}','YES'     ,''    ,'{kequipnum}',''       ,c.custrcl,''       ,''       ,1          ,1          ,'20'' OFFICE-STORAGE COMBO UNIT','c0001000' ,'40'      ,''          ,''          ,''        ,250      ,''          ,''       ,0       ,''        ,''    ,{newKordnum},''          ,''        ,'N'       ,''       ,'40'      ,0           ,''     ,'0'       ,'0'         ,''     ,''     ,'0'        ,0        ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,'1'        ,''       ,''     ,''       ,''       ,''       ,''       ,''       ,''       ,''       ,''       ,''       ,0         ,0         ,250       ,250        ,250         ,'2040'  ,'{rechead.Date__c?.AddDays(28).ToString("yyyyMMdd HH:mm:ss")}','{rechead.Date__c?.ToString("yyyyMMdd HH:mm:ss")}','20'     ,'R'         ,'4'     ,'R'    ,''        ,250        ,250         ,250         ,0           ,0           ,0           ,0           ,0           ,0           ,250         ,''       ,''          ,''     ,''       ,''     ,''      ,1       ,0         ,0         ,0         ,0         ,0         ,0         ,0         ,0         ,0         ,''         ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''        ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,''          ,0        ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,0          ,NULL       ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,NULL        ,'0'
FROM c


EXEC msdb.dbo.startjob @job = 'SF_MOBILE653_Reservation'

";
            Log.Information($"ExecuteScalarAsync (main qry): \r\n{sql}");
            var ret = await _rawSql.ExecuteScalarAsync<string>(sql);
            */

        }
        catch (Exception ex)
        {
            Log.Error(ex, $"OnQuoteChanged failed for Record {changeRecord.SalesforceRecordId}");
        }
    }

    public async Task OnQuoteLineItemChangedAsync(ChangeEventRecord record)
    {
        try
        {
            var results = await QueryAsync(
                $@"SELECT Id, IsDeleted, LineNumber, CreatedDate, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp, LastViewedDate, LastReferencedDate, QuoteId, PricebookEntryId, OpportunityLineItemId, Quantity, UnitPrice, Discount, Description, ServiceDate, Product2Id, SortOrder, ListPrice, Subtotal, TotalPrice, ProductID__c, SerialNumber__c, UID__c, Inventory__c
                   FROM QuoteLineItem
                   WHERE Id = '{record.SalesforceRecordId}'
                   ALL ROWS");

            if (results.Count == 0)
            {
                Log.Warning($"OnQuoteLineItemChanged: LineItem {record.SalesforceRecordId} not found");
                return;
            }

            var lineItem = results[0];

            System.Diagnostics.Debug.Print($"ChangeType: {record.ChangeType}");
            Log.Information($"OnQuoteLineItemChanged: LineItem {record.SalesforceRecordId} | Quote: {lineItem["QuoteId"]} | ChangeType: {record.ChangeType} | Qty: {lineItem["Quantity"]} | Price: {lineItem["UnitPrice"]}");

            sfQuoteLineItem sfo;
            try
            {
                sfo = lineItem.ToObject<sfQuoteLineItem>();
                var lstsfo = new List<sfQuoteLineItem> { sfo };
                //await _storageService.SaveQuoteItem(sfo);
                await _storageService.SaveQuoteItems(lstsfo, sfo.QuoteId);
                Log.Information($"Saved {sfo.GetType().Name} {record.SalesforceRecordId} to database");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deserialize sfQuoteLineItem {record.SalesforceRecordId}");
                return;
            }

            if (sfo != null)
            {
                var results2 = await QueryAsync(
                    $@"SELECT Condition__c, CreatedById, CreatedDate, Description, DisplayUrl, ExternalDataSourceId, ExternalId, Family, Id, IsActive, IsArchived, IsDeleted, LastModifiedById, LastModifiedDate, LastReferencedDate, LastViewedDate, Name, ProductCode, ProductID__c, Product_Code__c, Product_Group__c, QuantityUnitOfMeasure, Revenue_Type__c, StockKeepingUnit, SystemModstamp
                   FROM Product2
                   WHERE Id = '{sfo.Product2Id}'
                   ALL ROWS");

                if (results2.Count == 0)
                {
                    Log.Warning($"OnQuoteLineItemChanged: Product2Id {sfo.Product2Id} not found");
                    return;
                }

                var lineItem2 = results2[0];
                Log.Information($"OnQuoteLineItemChanged: Retrieved product {sfo.Product2Id}");

                sfProduct2 sfo2;
                try
                {
                    sfo2 = lineItem2.ToObject<sfProduct2>();
                    await _storageService.SaveProduct2(sfo2);
                    Log.Information($"Saved {sfo2.GetType().Name} {sfo2.Id} to database");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to deserialize sfProduct2 {sfo.Product2Id}");
                    return;
                }


                //if (record.ChangeType == "DELETE")
                //{
                //    await _storageService.DeleteQuoteItem(sfo.Id);
                //    Log.Information($"Deleted {sfo.GetType().Name} {sfo.Id} from database");
                //}
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"OnQuoteLineItemChanged failed for Record {record.SalesforceRecordId}");
        }
    }

    public async Task OnOpportunityChangedAsync(ChangeEventRecord record)
    {
        try
        {
            var results = await QueryAsync(
                $@"SELECT Id, IsDeleted, AccountId, Name, Description, StageName, Amount, Probability, CloseDate, Type, NextStep, LeadSource, IsClosed, IsWon, ForecastCategory, ForecastCategoryName, CampaignId, HasOpportunityLineItem, Pricebook2Id, OwnerId, CreatedDate, AgeInDays, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp, LastActivityDate, LastActivityInDays, PushCount, LastStageChangeDate, LastStageChangeInDays, FiscalQuarter, FiscalYear, Fiscal, ContactId, LastViewedDate, LastReferencedDate, SyncedQuoteId, ContractId, HasOpenActivity, HasOverdueTask, LastAmountChangedHistoryId, LastCloseDateChangedHistoryId, IsPriorityRecord, Budget_Confirmed__c, Discovery_Completed__c, ROI_Analysis_Completed__c, Loss_Reason__c, OpportunityIndustry__c, Opportunity_Industry__c
                   FROM Opportunity
                   WHERE Id = '{record.SalesforceRecordId}'");

            if (results.Count == 0)
            {
                Log.Warning($"OnOpportunityChanged: Opportunity {record.SalesforceRecordId} not found");
                return;
            }

            var opp = results[0];
            Log.Information($"OnOpportunityChanged: Opp {record.SalesforceRecordId} | Stage: {opp["StageName"]} | Amount: {opp["Amount"]}");

            //if (opp["StageName"]?.ToString() == "Closed Won") { await _storageService.MarkAsProcessedAsync(record.Id); }

            try
            {
                sfOpportunity sfo = opp.ToObject<sfOpportunity>();
                await _storageService.SaveOpp(sfo);
                Log.Information($"Saved {sfo.GetType().Name} {record.SalesforceRecordId} to database");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deserialize sfOpportunity {record.SalesforceRecordId}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"OnOpportunityChanged failed for Record {record.SalesforceRecordId}");
        }
    }

    public async Task OnOpportunityLineItemChangedAsync(ChangeEventRecord record)
    {
        try
        {
            var results = await QueryAsync(
                $@"SELECT Id, OpportunityId, SortOrder, PricebookEntryId, Product2Id, ProductCode, Name, Quantity, TotalPrice, UnitPrice, ListPrice, ServiceDate, Description, CreatedDate, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp, IsDeleted, LastViewedDate, LastReferencedDate
                   FROM OpportunityLineItem
                   WHERE Id = '{record.SalesforceRecordId}'");

            if (results.Count == 0)
            {
                Log.Warning($"OnOpportunityLineItemChanged: LineItem {record.SalesforceRecordId} not found");
                return;
            }

            var lineItem = results[0];
            Log.Information($"OnOpportunityLineItemChanged: LineItem {record.SalesforceRecordId} | Opp: {lineItem["OpportunityId"]} | Qty: {lineItem["Quantity"]} | Price: {lineItem["UnitPrice"]}");

            try
            {
                sfOpportunityLineItem sfo = lineItem.ToObject<sfOpportunityLineItem>();
                await _storageService.SaveOppItem(sfo);
                Log.Information($"Saved {sfo.GetType().Name} {record.SalesforceRecordId} to database");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deserialize OpportunityLineItem {record.SalesforceRecordId}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"OnOpportunityLineItemChanged failed for Record {record.SalesforceRecordId}");
        }
    }

    public async Task OnAccountChangedAsync(ChangeEventRecord record)
    {
        try
        {
            var results = await QueryAsync(
                $@"SELECT Id, IsDeleted, MasterRecordId, Name, Type, ParentId, BillingStreet, BillingCity, BillingState, BillingPostalCode, BillingCountry, BillingLatitude, BillingLongitude, BillingGeocodeAccuracy, ShippingStreet, ShippingCity, ShippingState, ShippingPostalCode, ShippingCountry, ShippingLatitude, ShippingLongitude, ShippingGeocodeAccuracy, Phone, Website, PhotoUrl, Industry, NumberOfEmployees, Description, OwnerId, CreatedDate, CreatedById, LastModifiedDate, LastModifiedById, SystemModstamp, LastActivityDate, LastViewedDate, LastReferencedDate, Jigsaw, JigsawCompanyId, AccountSource, SicDesc, Account_Number__c, EBS_Customer_ID__c, Term_Length__c, LastModifiedDate__c
                   FROM Account
                   WHERE Id = '{record.SalesforceRecordId}'");
            if (results.Count == 0)
            {
                Log.Warning($"OnAccountChanged: Account {record.SalesforceRecordId} not found");
                return;
            }
            var account = results[0];
            Log.Information($"OnAccountChanged: Account {record.SalesforceRecordId} | Name: {account["Name"]} | Type: {account["Type"]}");
            try
            {
                sfAccount sfo = account.ToObject<sfAccount>();
                await _storageService.SaveAccount(sfo);
                Log.Information($"Saved {sfo.GetType().Name} {record.SalesforceRecordId} to database");
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to deserialize sfAccount {record.SalesforceRecordId}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"OnAccountChanged failed for Record {record.SalesforceRecordId}");
        }
    }

    // -------------------------------------------------------------------------
    // Custom Notification Action - send notifications to Salesforce users
    // -------------------------------------------------------------------------


    /// <summary>
    /// Looks up a CustomNotificationType Id by its API Name.
    /// Result is cached in memory for the lifetime of the service.
    /// </summary>
    /// <param name="apiName">The DeveloperName / ApiName of the Custom Notification Type (e.g. "Quote_Update").</param>
    /// <returns>The 18-character Salesforce Id of the notification type (starts with "0ML").</returns>
    public async Task<string> GetNotificationTypeIdAsync(string apiName)
    {
        if (_notifTypeIdCache.TryGetValue(apiName, out var cached))
            return cached;

        var results = await QueryAsync(
            $"SELECT Id, DeveloperName FROM CustomNotificationType WHERE DeveloperName = '{apiName}'");

        if (results.Count == 0)
            throw new Exception($"CustomNotificationType '{apiName}' not found in Salesforce");

        var id = results[0]["Id"]!.ToString()!;
        _notifTypeIdCache[apiName] = id;

        Log.Information($"Resolved CustomNotificationType '{apiName}' to Id {id}");
        return id;
    }

    /// <summary>
    /// Sends a Salesforce Custom Notification via the standard customNotificationAction.
    /// Wraps all 5 input fields of the action as parameters.
    /// </summary>
    /// <param name="customNotifTypeId">
    ///   Either the 18-character Id of the CustomNotificationType (starts with "0ML"),
    ///   OR the DeveloperName/ApiName (e.g. "Quote_Update") - in which case the Id is resolved automatically.
    /// </param>
    /// <param name="recipientIds">List of User Ids (or Group Ids) to receive the notification. Required.</param>
    /// <param name="title">The notification title shown to recipients. Required.</param>
    /// <param name="body">The notification body text. Required.</param>
    /// <param name="targetId">
    ///   Optional Salesforce record Id to associate the notification with - clicking the notification
    ///   will open this record. Pass null for general notifications not tied to any specific record.
    /// </param>
    /// <returns>
    ///   The JArray returned by Salesforce - each element corresponds to one input invocation,
    ///   with an "isSuccess" boolean and any error details.
    /// </returns>
    /// <example>
    /// // Status update tied to the changed Quote record
    /// await _queryService.SendCustomNotificationAsync(
    ///     customNotifTypeId: "Quote_Update",
    ///     recipientIds:      new List&lt;string&gt; { ownerId },
    ///     title:             "Quote Approved",
    ///     body:              $"Quote {record.SalesforceRecordId} has been approved",
    ///     targetId:          record.SalesforceRecordId);
    ///
    /// // General error alert with no associated record
    /// await _queryService.SendCustomNotificationAsync(
    ///     customNotifTypeId: "System_Alert",
    ///     recipientIds:      new List&lt;string&gt; { adminUserId },
    ///     title:             "Integration error",
    ///     body:              $"Failed to process Quote {quoteId}: {ex.Message}",
    ///     targetId:          null);
    /// </example>
    public async Task<JArray> SendCustomNotificationAsync(
        string customNotifTypeId,
        List<string> recipientIds,
        string title,
        string body,
        string? targetId)
    {
        // If caller passed an API name rather than an Id, resolve it
        if (!customNotifTypeId.StartsWith("0ML", StringComparison.OrdinalIgnoreCase))
            customNotifTypeId = await GetNotificationTypeIdAsync(customNotifTypeId);

        var (token, instanceUrl) = await _authService.GetTokenAsync();

        var url = $"{instanceUrl}/services/data/v{ApiVersion}/actions/standard/customNotificationAction";

        // Build the action input - all 5 fields
        var input = new Dictionary<string, object?>
        {
            ["customNotifTypeId"] = customNotifTypeId,
            ["recipientIds"] = recipientIds,
            ["title"] = title,
            ["body"] = body
        };

        // targetId is optional - only include it if supplied
        if (!string.IsNullOrEmpty(targetId))
            input["targetId"] = targetId;

        var payload = new { inputs = new[] { input } };
        var json = JsonConvert.SerializeObject(payload);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {token}");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Error($"SendCustomNotification failed [{(int)response.StatusCode}]: {responseJson} | Title: {title}");
                throw new Exception($"SendCustomNotification failed ({response.StatusCode}): {responseJson}");
            }

            var results = JsonConvert.DeserializeObject<JArray>(responseJson) ?? new JArray();

            // Inspect each result for per-input failures (the HTTP call can be 200 even if individual inputs failed)
            foreach (var result in results)
            {
                var isSuccess = result["isSuccess"]?.ToObject<bool>() ?? false;
                if (!isSuccess)
                {
                    var errors = result["errors"]?.ToString() ?? "unknown";
                    Log.Warning($"Custom notification input failed | Title: {title} | Errors: {errors}");
                }
            }

            Log.Information($"Sent custom notification '{title}' to {recipientIds.Count} recipient(s)");
            return results;
        }
        catch (Exception ex) when (ex is not Exception { Message: var m } || !m.StartsWith("SendCustomNotification failed"))
        {
            Log.Error(ex, $"SendCustomNotification failed | Title: {title}");
            throw;
        }
    }
}
