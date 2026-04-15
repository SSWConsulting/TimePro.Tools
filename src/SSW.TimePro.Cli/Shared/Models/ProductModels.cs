namespace SSW.TimePro.Cli.Shared.Models;

/// <summary>
/// Product (PRODUCT-category, not SKU) from /api/Product and /api/Product/{id}.
///
/// Per TimePRO's naming map: database "ProdCategory" = API "Product".
/// </summary>
public class ProductRow
{
    public string? ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? Head { get; set; }
    public string? Note { get; set; }
    public bool AllowDiscount { get; set; }
    public bool AllowDiscount2 { get; set; }
    public bool DisplayOnWeb { get; set; }
    public bool IsTraining { get; set; }
    public bool? IsPopular { get; set; }
    public int? Color { get; set; }
    public int? Sort { get; set; }
    public string? Url { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public List<ProductSkuRow>? Skus { get; set; }
}

/// <summary>
/// SKU ("Prod" in the database) — returned by /api/Product/All and nested under
/// ProductRow.Skus when /api/Product?isExpand=true is called.
/// </summary>
public class ProductSkuRow
{
    public string? SkuId { get; set; }
    public string? SkuName { get; set; }
    public string? ProductId { get; set; }
    public decimal? SellAmt { get; set; }
    public decimal? CostAmt { get; set; }
    public decimal? RrpAmt { get; set; }
    public bool? IsPrepaid { get; set; }
    public bool? DisplayOnWeb { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
}

/// <summary>
/// Discount row from /api/Product/GetDiscountsForClient/{clientId}.
/// </summary>
public class ProductDiscountRow
{
    public string? ProductId { get; set; }
    public string? SkuId { get; set; }
    public string? ClientId { get; set; }
    public decimal? DiscountPct { get; set; }
    public decimal? DiscountAmt { get; set; }
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }
    public string? Note { get; set; }
}
