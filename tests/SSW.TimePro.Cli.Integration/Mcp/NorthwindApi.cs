using System.Text.Json;
using SSW.TimePro.Cli.Shared.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SSW.TimePro.Cli.Integration.Mcp;

/// <summary>
/// One fake TimePro instance, all Northwind, shared by every MCP tool and CLI parity case.
///
/// Response bodies are serialised from the real DTOs rather than hand-written JSON: a fixture
/// that no longer binds would otherwise show up as a golden full of nulls instead of a failure.
/// </summary>
public static class NorthwindApi
{
    public const string ClientId = "NWIND";
    public const string ClientName = "Northwind Traders";
    public const string ProjectId = "1I776Q";
    public const string EmpId = "BOB";
    public const string EmpName = "Bob Northwind";
    public const string EmpEmail = "bob@northwind.example";

    public const int TimesheetId = 4242;
    public const int SuggestedTimesheetId = 4243;
    public const int InvoiceId = 108;
    public const int ReceiptId = 142;
    public const int RecurringInvoiceId = 42;
    public const string LeaveId = "0f2b7e7a-1111-4444-8888-aaaaaaaaaaaa";

    public const string AnyDate = "2026-03-16";

    /// <summary>Baseline stubs sit below per-case overrides so a case can replace one route.</summary>
    public const int BaselinePriority = 10;
    public const int OverridePriority = 1;

    private static readonly JsonSerializerOptions Body = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Registers every route the 47 MCP tools can reach, populated with Northwind data.</summary>
    public static void StubAll(WireMockServer server)
    {
        StubIdentity(server);
        StubLookups(server);
        StubTimesheets(server);
        StubLeave(server);
        StubAccountingInvoices(server);
        StubAccountingReceipts(server);
        StubAccountingProducts(server);
        StubAccountingMisc(server);
    }

    public static void Json(WireMockServer server, string path, string method, object body, int priority = BaselinePriority) =>
        server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .AtPriority(priority)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(body, Body)));

    public static void RawJson(WireMockServer server, string path, string method, string body, int priority = BaselinePriority) =>
        server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .AtPriority(priority)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    public static void Status(WireMockServer server, string path, string method, int statusCode, string body = "", int priority = BaselinePriority) =>
        server.Given(Request.Create().WithPath(path).UsingMethod(method))
            .AtPriority(priority)
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    // ───────────────────────── Identity ─────────────────────────

    private static void StubIdentity(WireMockServer server)
    {
        Json(server, "/api/Employees/GetEmployeeID", "GET", new EmployeeIdResponse { EmpId = EmpId });

        Json(server, "/api/v2/users/me", "GET", new CurrentUserResponse
        {
            EmployeeId = EmpId,
            FirstName = "Bob",
            LastName = "Northwind",
            Email = EmpEmail,
            DefaultRate = "160"
        });

        Json(server, "/api/employees/getSettingsDetails", "GET", new EmployeeSettings
        {
            EmployeeId = EmpId,
            StartTime = "09:00",
            EndTime = "17:00",
            LunchBreakStart = "12:00",
            LunchBreakEnd = "12:30",
            TimeLessMinutes = 30,
            TimezoneId = "Australia/Brisbane"
        });
    }

    // ───────────────────────── Lookups ─────────────────────────

    private static void StubLookups(WireMockServer server)
    {
        Json(server, "/api/Timesheets/GetClientListForAddTimesheet", "GET", new List<ClientSearchResult>
        {
            new() { Value = ClientId, Text = ClientName }
        });

        Json(server, "/api/Projects/GetSelectListUsageDataProject", "GET", new List<ProjectForSelect>
        {
            new() { Value = ProjectId, DisplayText = ClientName, UseIteration = true },
            new() { Value = "NW0002", DisplayText = "Northwind Storefront", UseIteration = false }
        });

        Json(server, "/api/Timesheets/GetClientRate", "GET", new ClientRateResponse
        {
            EmpId = EmpId,
            ClientId = ClientId,
            ClientName = ClientName,
            EmployeeName = EmpName,
            Rate = 175m,
            PrepaidRate = 150m,
            ClientRateId = 900,
            ExpiryDate = "2026-12-31",
            Notes = "Standard rate"
        });

        RawJson(server, $"/api/v2/clients/{ClientId}/taxrates", "GET", "0.1");

        Json(server, "/Crm/Appointments", "GET", new List<AppointmentItem>
        {
            new()
            {
                Id = "appt-1",
                Title = "Northwind Traders - Checkout API review",
                Start = "2026-03-16T09:00:00",
                End = "2026-03-16T10:00:00",
                AllDay = false,
                ClientId = ClientId,
                ProjectId = ProjectId,
                IterationId = 3402,
                Editable = true,
                TimeZoneOffsetInMinutes = 600
            }
        });

        Json(server, "/api/Timesheets/GetTimesheetLocation", "GET", new List<TimesheetLocation>
        {
            new() { LocationId = "SSW", LocationName = "SSW" },
            new() { LocationId = "Home", LocationName = "Home" }
        });

        Json(server, "/api/Timesheets/GetTimesheetCategories", "GET", new List<TimesheetCategory>
        {
            new() { CategoryId = "WEBDEV", CategoryName = "Web development", IsNonWorking = false },
            new() { CategoryId = "TRAIN", CategoryName = "Training", IsNonWorking = false }
        });

        Json(server, "/api/Timesheets/GetTimesheetBillableType", "GET", new List<TimesheetBillableType>
        {
            new() { Value = "B", Text = "Billable" },
            new() { Value = "BPP", Text = "Prepaid" },
            new() { Value = "W", Text = "Write-off" }
        });

        Json(server, "/api/ProjectIteration/GetIterationsForAddTimesheet", "GET", new List<IterationItem>
        {
            new() { IterationId = 3402, IterationName = "Checkout API", LastUsedDate = "2026-03-09" },
            new() { IterationId = 3403, IterationName = "Order history", LastUsedDate = "2026-03-13" }
        });

        Json(server, "/api/Timesheets/GetProjectsSummary", "GET", new List<ProjectSummaryItem>
        {
            new()
            {
                ProjectName = ClientName,
                ClientID = ClientId,
                ProjectID = ProjectId,
                IsBillable = true,
                Sum = 22.5m,
                StartDate = "2026-03-01",
                EndDate = "2026-03-31"
            }
        });
    }

    // ───────────────────────── Timesheets ─────────────────────────

    public static List<TimesheetItem> Day(bool isSuggested = false) =>
    [
        new()
        {
            TimeId = TimesheetId,
            EmpId = EmpId,
            EmpName = EmpName,
            Client = ClientName,
            ClientId = ClientId,
            Project = ClientName,
            ProjectId = ProjectId,
            Iteration = "Checkout API",
            IterationId = null,
            Category = "Web development",
            Location = "Home",
            LocationId = "Home",
            Notes = "Product search",
            Date = "2026-03-16T00:00:00",
            StartTime = "2026-03-16T09:00:00",
            EndTime = "2026-03-16T17:00:00",
            BillableId = "B",
            IsBillable = true,
            Less = 0.5m,
            TotalTime = 7.5m,
            HasNotes = true,
            IsSuggested = isSuggested,
            IsLeave = false,
            InvoiceId = null,
            IsLocked = false
        },
        new()
        {
            TimeId = SuggestedTimesheetId,
            EmpId = EmpId,
            EmpName = EmpName,
            Client = ClientName,
            ClientId = ClientId,
            Project = ClientName,
            ProjectId = ProjectId,
            Iteration = "Order history",
            IterationId = 3403,
            Category = "Web development",
            Location = "SSW",
            LocationId = "SSW",
            Notes = "Order history",
            Date = "2026-03-16T00:00:00",
            StartTime = "2026-03-16T13:00:00",
            EndTime = "2026-03-16T14:00:00",
            BillableId = "B",
            IsBillable = true,
            Less = 0m,
            TotalTime = 1m,
            HasNotes = true,
            IsSuggested = true,
            IsLeave = false,
            IsLocked = false
        }
    ];

    private static void StubTimesheets(WireMockServer server)
    {
        Json(server, "/api/Timesheets/GetTimesheetListViewModel", "GET", Day());

        Json(server, "/api/timesheetSummary/GetTableSummarydata", "POST", new List<TimesheetSummaryEntry>
        {
            new()
            {
                TimeId = TimesheetId,
                TimesheetDate = "2026-03-16",
                EmpId = EmpId,
                EmployeeName = EmpName,
                BillableId = "B",
                ClientId = ClientId,
                ClientName = ClientName,
                Description = "Product search",
                ProjectId = ProjectId,
                ProjectName = ClientName,
                CategoryId = "WEBDEV",
                CategoryName = "Web development",
                Note = "Product search",
                TotalHours = 7.5m,
                SellPrice = 175m
            }
        });

        RawJson(server, "/api/Timesheets/SaveTimesheet", "POST", string.Empty);
        Status(server, "/api/Timesheets/RefreshSuggestedTimesheets", "GET", 200);
        Status(server, $"/api/Timesheets/DeleteTimesheet/{TimesheetId}", "DELETE", 200);

        Json(server, "/api/Timesheets/AcceptSuggestedTimesheet", "POST", new TimesheetResponse
        {
            Success = true,
            Message = "Accepted",
            TimesheetId = TimesheetId
        });
    }

    // ───────────────────────── Leave ─────────────────────────

    public static LeaveEntry LeaveEntryFixture() => new()
    {
        Id = LeaveId,
        StartDate = "2026-04-01T00:00:00+10:00",
        EndDate = "2026-04-01T23:59:00+10:00",
        CreatedAt = "2026-03-01T09:00:00+10:00",
        UpdatedAt = "2026-03-02T09:00:00+10:00",
        Note = "Family day",
        ApprovedBy = EmpEmail,
        OptionalEmp = [EmpEmail],
        RequestedEmpId = EmpId,
        UserStartTime = "09:00",
        UserEndTime = "18:00",
        DaysAway = 1m,
        AllDay = true,
        Length = 1m,
        LeaveStatus = 1,
        LeaveType = new LeaveTypeInfo { Id = 1, Name = "Annual Leave", IsActive = true }
    };

    /// <summary>
    /// Serves a different row on the PAST filter, so the ALL filter's merge and de-duplication
    /// are actually exercised instead of being handed the same entry twice.
    /// </summary>
    public static void StubDistinctPastLeave(WireMockServer server)
    {
        var past = LeaveEntryFixture();
        past.Id = "1a3c8f8b-2222-4444-8888-bbbbbbbbbbbb";
        past.StartDate = "2026-02-02T00:00:00+10:00";
        past.EndDate = "2026-02-02T23:59:00+10:00";
        past.Note = "Public holiday";

        server.Given(Request.Create()
                .WithPath("/api/leave/")
                .WithParam("leaveFilter", "PAST")
                .UsingGet())
            .AtPriority(OverridePriority)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(JsonSerializer.Serialize(new LeaveListResponse
                {
                    CancelledCount = 0,
                    Leaves = new PaginatedList<LeaveEntry>
                    {
                        PageNumber = 1,
                        PageSize = 10,
                        TotalItems = 1,
                        TotalPages = 1,
                        Items = [past]
                    }
                }, Body)));
    }

    private static void StubLeave(WireMockServer server)
    {
        Json(server, "/api/leave/", "GET", new LeaveListResponse
        {
            CancelledCount = 1,
            Leaves = new PaginatedList<LeaveEntry>
            {
                PageNumber = 1,
                PageSize = 10,
                TotalItems = 1,
                TotalPages = 1,
                Items = [LeaveEntryFixture()]
            }
        });

        Json(server, "/api/leave/types", "GET", new List<LeaveTypeInfo>
        {
            new() { Id = 1, Name = "Annual Leave", IsActive = true },
            new() { Id = 2, Name = "Sick Leave", IsActive = true }
        });

        Json(server, $"/api/leave/stats/{EmpId}", "GET", new LeaveStats
        {
            DaysSinceLastLeave = 42
        });

        Status(server, "/api/leave/", "POST", 200);
        Status(server, "/api/leave/", "PUT", 200);

        Json(server, "/api/leave/balances/status", "GET", new LeaveBalanceStatus
        {
            AsAtDate = new DateOnly(2026, 3, 1),
            LastImportedAt = new DateTimeOffset(2026, 3, 2, 4, 30, 0, TimeSpan.Zero),
            EmployeeCount = 12,
            IsStale = false
        });

        Json(server, "/api/leave/balances/import", "POST", new ImportLeaveBalancesResult
        {
            AsAtDate = new DateOnly(2026, 3, 1),
            Created = 1,
            Updated = 2,
            UnmatchedEmployees = ["Bobby Northwind"],
            Warnings = ["Implausible balance for BOB: 999 days"]
        });
    }

    // ───────────────────────── Accounting: invoices ─────────────────────────

    public static InvoiceHeader InvoiceFixture(int invoiceId = InvoiceId) => new()
    {
        InvoiceId = invoiceId,
        ClientId = ClientId,
        ProjectId = ProjectId,
        CategoryId = "WEBDEV",
        CurrencyId = "AUD",
        ExchangeRate = 1,
        InvoiceType = "P",
        Batch = 1,
        ClientRef = "PO-42",
        DateStart = new DateTime(2026, 3, 1),
        DateEnd = new DateTime(2026, 3, 31),
        SubTotal = 1000m,
        SellTotal = 1100m,
        SalesTaxPct = 10,
        SalesTaxAmt = 100m,
        PaidAmt = 0m,
        OSAmt = 1100m,
        DateCreated = new DateTime(2026, 3, 31),
        DateUpdated = new DateTime(2026, 3, 31),
        Note = "Prepaid block",
        Month = "2026-03",
        PaymentTerms = 14,
        IsRecurring = false,
        IsLocked = false,
        IsCreditNote = false,
        OutstandingDays = 7,
        RemainingPrepaidCredit = 400m
    };

    public static InvoiceTimesheet InvoiceTimesheetFixture(string billableId = "BPP") => new()
    {
        TimeId = TimesheetId,
        EmpId = EmpId,
        EmpName = EmpName,
        ClientId = ClientId,
        ClientName = ClientName,
        ProjectId = ProjectId,
        ProjectName = ClientName,
        CategoryId = "WEBDEV",
        BillableId = billableId,
        DateCreated = new DateTime(2026, 3, 16),
        TimeStart = "09:00",
        TimeEnd = "17:00",
        TotalTime = 7.5m,
        Amount = 600m,
        BillableAmount = 600m,
        SellPrice = 80m,
        SellTotal = 600m,
        SalesTaxAmt = 60m,
        SalesTaxPct = 10,
        Notes = "Checkout API",
        InvoiceId = InvoiceId
    };

    private static void StubAccountingInvoices(WireMockServer server)
    {
        Json(server, "/api/ClientInvoice/rangepaged", "GET", new PagedResponse<InvoiceSearchRow>
        {
            Total = 1,
            Data =
            [
                new()
                {
                    InvoiceId = InvoiceId,
                    DateCreated = new DateTime(2026, 3, 31),
                    InvoiceType = "P",
                    ClientId = ClientId,
                    CoName = ClientName,
                    SellTotal = 1100m,
                    PaidAmt = 0m,
                    ExternalSyncStatus = 0
                }
            ]
        });

        Json(server, $"/api/v2/ClientInvoice/{InvoiceId}", "GET", InvoiceFixture());

        Json(server, $"/api/v2/ClientInvoice/{InvoiceId}/products", "GET", new List<InvoiceLine>
        {
            new()
            {
                InvoiceProdId = 1,
                InvoiceId = InvoiceId,
                SkuId = "PREPAID-10",
                SkuName = "Prepaid 10 hours",
                ProdName = "Prepaid block",
                CategoryId = "WEBDEV",
                EmpId = EmpId,
                Qty = 1,
                SellAmt = 1000m,
                SellTotal = 1000m,
                SalesTaxAmt = 100m,
                SalesTaxPct = 10,
                Type = "S"
            }
        });

        Json(server, "/api/v2/Timesheets/WithNames/Allocated", "GET",
            new List<InvoiceTimesheet> { InvoiceTimesheetFixture() });
        Json(server, "/api/v2/Timesheets/WithNames/Writeoff", "GET",
            new List<InvoiceTimesheet> { InvoiceTimesheetFixture("W") });
        Json(server, "/api/v2/Timesheets/WithNames/Unallocated", "GET",
            new List<InvoiceTimesheet> { InvoiceTimesheetFixture("B") });

        Json(server, $"/api/v2/ClientInvoice/{InvoiceId}/receipts", "GET", new List<ReceiptRow>
        {
            new()
            {
                SaleReceiptId = ReceiptId,
                InvoiceId = InvoiceId,
                Paid = 1100m,
                CoName = ClientName,
                DateCreated = new DateTime(2026, 4, 1),
                PaymentDate = new DateTime(2026, 4, 1),
                EmpUpdated = EmpId,
                Note = "EFT",
                SaleReceiptStatus = "Allocated"
            }
        });

        Json(server, $"/api/ClientInvoice/ClientID/{ClientId}", "GET",
            new List<InvoiceHeader> { InvoiceFixture() });

        Json(server, $"/api/ClientInvoice/GetByClientId/{ClientId}", "GET", new ClientInvoiceTable
        {
            Total = 1,
            Invoices = [InvoiceFixture()]
        });

        Json(server, $"/api/ClientInvoice/UnpaidByClientID/{ClientId}", "GET",
            new List<InvoiceHeader> { InvoiceFixture() });

        server.Given(Request.Create().WithPath("/Reporting/GetPrepaidStatusReport").UsingGet())
            .AtPriority(BaselinePriority)
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody("%PDF-1.4 northwind"));
    }

    // ───────────────────────── Accounting: receipts ─────────────────────────

    private static void StubAccountingReceipts(WireMockServer server)
    {
        Json(server, "/api/receipting/PaidReceiptsPaged", "GET", new PagedResponse<PaidReceiptRow>
        {
            Total = 1,
            Data =
            [
                new()
                {
                    SaleReceiptId = ReceiptId,
                    ClientId = ClientId,
                    CoName = ClientName,
                    PaymentDate = new DateTime(2026, 4, 1),
                    DateCreated = new DateTime(2026, 4, 1),
                    EmpUpdated = EmpId,
                    Note = "EFT",
                    Bank = "Northwind Bank",
                    Unallocated = 0m,
                    PaidTotal = -1100m,
                    InvoiceIds = [InvoiceId],
                    SaleReceiptPaids = [],
                    SaleReceiptType = new ReceiptTypeInfo { Id = "EFT", TypeName = "EFT", TypeSign = "-" }
                }
            ]
        });

        Json(server, $"/api/Receipting/details/{ReceiptId}", "GET", new ReceiptDetailResponse
        {
            TotalPaid = 1100m,
            InvoicesAmount = 1100m,
            ContactPerson = "Bob Northwind",
            PaymentMethods = [new ReceiptPaymentMethod { Id = "EFT", Name = "EFT", Sign = "-" }],
            Receipt = new ReceiptDetail
            {
                SaleReceiptId = ReceiptId,
                ReceiptType = "EFT",
                ClientId = ClientId,
                CoName = ClientName,
                PaymentDate = new DateTime(2026, 4, 1),
                IsNew = false,
                ReceiptTotal = 1100m,
                Note = "EFT",
                SaleReceiptPaids =
                [
                    new()
                    {
                        InvoiceId = InvoiceId,
                        SaleReceiptId = ReceiptId,
                        InvoiceDate = new DateTime(2026, 3, 31),
                        PaymentDate = new DateTime(2026, 4, 1),
                        Total = 1100m,
                        AlreadyPaidAmt = 0m,
                        PaidAmt = 1100m,
                        Balance = 0m,
                        IsAllocated = true,
                        ClientId = ClientId,
                        CoName = ClientName
                    }
                ],
                OutstandingInvoices = []
            }
        });

        Json(server, $"/api/Receipting/ClientOutstanding/{ClientId}", "GET", new ClientOutstandingSummary
        {
            ClientId = ClientId,
            CoName = ClientName,
            ContactPerson = "Bob Northwind",
            OutstandingInvoices =
            [
                new()
                {
                    InvoiceId = InvoiceId,
                    DateInvoiced = new DateTime(2026, 3, 31),
                    DueDate = new DateTime(2026, 4, 14),
                    Total = 1100m,
                    PaidAmt = 0m,
                    OsAmt = 1100m,
                    DaysOverdue = 7,
                    InvoiceType = "P"
                }
            ]
        });

        Json(server, $"/api/creditnote/by-client/{ClientId}", "GET", new List<CreditNoteRow>
        {
            new()
            {
                Id = 7,
                Amount = 110m,
                Note = "Goodwill credit",
                CreditNoteDate = new DateTime(2026, 4, 2),
                TaxRate = 10m,
                IsLocked = false,
                Paid = 0m,
                SyncStatus = 0,
                SyncDisplayName = "Not synced",
                IsCreditingInvoice = true,
                AssociatedInvoiceId = InvoiceId
            }
        });

        Json(server, "/api/clients/OutstandingTime", "GET", new List<ClientOutstandingTimeRow>
        {
            new()
            {
                ClientId = ClientId,
                CoName = ClientName,
                EmpId = EmpId,
                FirstName = "Bob",
                Surname = "Northwind",
                Suburb = "Brisbane",
                State = "QLD",
                Os = 600m,
                Billable = 600m,
                DateUpdated = new DateTime(2026, 3, 16),
                EarliestUnAllocatedTimesheetDate = new DateTime(2026, 3, 16)
            }
        });
    }

    // ───────────────────────── Accounting: products ─────────────────────────

    private static void StubAccountingProducts(WireMockServer server)
    {
        var sku = new ProductSkuRow
        {
            SkuId = "PREPAID-10",
            SkuName = "Prepaid 10 hours",
            ProductId = "PREPAID",
            SellAmt = 1000m,
            CostAmt = 0m,
            RrpAmt = 1000m,
            IsPrepaid = true,
            DisplayOnWeb = false,
            DateCreated = new DateTime(2026, 1, 1),
            DateUpdated = new DateTime(2026, 1, 1)
        };

        Json(server, "/api/Product", "GET", new List<ProductRow>
        {
            new()
            {
                ProductId = "PREPAID",
                ProductName = "Prepaid block",
                Head = "Prepaid",
                Note = "Prepaid hours",
                AllowDiscount = true,
                DisplayOnWeb = false,
                IsTraining = false,
                DateCreated = new DateTime(2026, 1, 1),
                DateUpdated = new DateTime(2026, 1, 1),
                Skus = [sku]
            }
        });

        Json(server, "/api/Product/PREPAID", "GET", new ProductRow
        {
            ProductId = "PREPAID",
            ProductName = "Prepaid block",
            Head = "Prepaid",
            AllowDiscount = true,
            DisplayOnWeb = false,
            IsTraining = false,
            DateCreated = new DateTime(2026, 1, 1),
            DateUpdated = new DateTime(2026, 1, 1),
            Skus = [sku]
        });

        Json(server, "/api/Product/All", "GET", new List<ProductSkuRow> { sku });

        Json(server, $"/api/Product/GetDiscountsForClient/{ClientId}", "GET", new List<ProductDiscountRow>
        {
            new()
            {
                ProductId = "PREPAID",
                SkuId = "PREPAID-10",
                ClientId = ClientId,
                DiscountPct = 5m,
                DiscountAmt = null,
                DateStart = new DateTime(2026, 1, 1),
                DateEnd = new DateTime(2026, 12, 31),
                Note = "Volume discount"
            }
        });
    }

    private static void StubAccountingMisc(WireMockServer server)
    {
        Json(server, "/api/clients/GetClientRates", "GET", new ClientRateTable
        {
            Total = 1,
            Rates =
            [
                new()
                {
                    ClientRateId = 900,
                    EmpId = EmpId,
                    EmployeeName = EmpName,
                    ClientId = ClientId,
                    ClientName = ClientName,
                    Rate = 175m,
                    PrepaidRate = 150m,
                    ExpiryDate = new DateTime(2026, 12, 31),
                    DateCreated = new DateTime(2026, 1, 1),
                    DateUpdated = new DateTime(2026, 1, 1),
                    Notes = "Standard rate"
                }
            ]
        });

        Json(server, "/api/recurring/invoices/", "GET", new PagedResponse<RecurringInvoiceRow>
        {
            Total = 1,
            Data =
            [
                new()
                {
                    Id = RecurringInvoiceId,
                    ClientId = ClientId,
                    ClientName = ClientName,
                    SellTotal = 1100m,
                    CountOfInv = 3,
                    Unit = RecurrenceUnit.Month,
                    Note = "Monthly retainer",
                    LastInvEndDate = new DateTime(2026, 3, 31),
                    IsActive = true,
                    CreatedOn = new DateTime(2026, 1, 1)
                }
            ]
        });

        Json(server, $"/api/recurring/invoices/{RecurringInvoiceId}", "GET", new RecurringInvoiceDetail
        {
            Id = RecurringInvoiceId,
            ClientId = ClientId,
            SellAmt = 1000m,
            SellTaxAmt = 100m,
            SellTotal = 1100m,
            CountOfInv = 3,
            FirstInvPeriod = 1,
            SubsequentInvPeriod = 1,
            LastInvPeriod = 1,
            DateStart = new DateTime(2026, 1, 1),
            DateEnd = new DateTime(2026, 12, 31),
            LastInvEndDate = new DateTime(2026, 3, 31),
            NextInvoicePeriodStart = new DateTime(2026, 4, 1),
            NextInvoicePeriodEnd = new DateTime(2026, 4, 30),
            CanGenerateNow = true,
            Unit = RecurrenceUnit.Month,
            Note = "Monthly retainer",
            CreatedBy = EmpId,
            CreatedOn = new DateTime(2026, 1, 1),
            Products =
            [
                new()
                {
                    Id = 1,
                    RecurringInvoiceId = RecurringInvoiceId,
                    ProdId = "PREPAID",
                    ProdName = "Prepaid block",
                    SellAmt = 1000m,
                    SellTotal = 1000m,
                    SalesTaxPct = 10,
                    SalesTaxAmt = 100m,
                    Qty = 1,
                    CreatedBy = EmpId,
                    CreatedOn = new DateTime(2026, 1, 1)
                }
            ]
        });
    }
}
