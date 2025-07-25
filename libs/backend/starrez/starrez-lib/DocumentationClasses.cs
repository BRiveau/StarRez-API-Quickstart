namespace StarRez;

public class StarRezEnum
{
    public required int enumId { get; set; }
    public required string description { get; set; }
}

public class StarRezHttpError
{
    public required string description { get; set; }
}

public class StarRezAccountBalance
{
    public required decimal TotalAmount { get; set; }
    public required decimal TotalTaxAmount { get; set; }
    public required decimal TotalTaxAmount2 { get; set; }
}
