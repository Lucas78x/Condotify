using System.ComponentModel;

public enum DeliveryTypeEnum
{
    // 📮 Correios
    [Description("Correios")]
    Correios = 1,

    [Description("SEDEX")]
    Sedex = 2,

    [Description("PAC")]
    PAC = 3,

    // 🚚 Transportadoras
    [Description("Jadlog")]
    Jadlog = 10,

    [Description("Total Express")]
    TotalExpress = 11,

    [Description("Azul Cargo")]
    AzulCargo = 12,

    [Description("TNT")]
    TNT = 13,

    [Description("DHL")]
    DHL = 14,

    [Description("FedEx")]
    FedEx = 15,

    [Description("UPS")]
    UPS = 16,

    [Description("Loggi")]
    Loggi = 17,

    // 🛒 Marketplaces
    [Description("Mercado Livre")]
    MercadoLivre = 30,

    [Description("Amazon")]
    Amazon = 31,

    [Description("Shopee")]
    Shopee = 32,

    [Description("AliExpress")]
    AliExpress = 33,

    [Description("Magazine Luiza")]
    MagazineLuiza = 34,

    [Description("Casas Bahia")]
    CasasBahia = 35,

    [Description("Ponto Frio")]
    PontoFrio = 36,

    [Description("KaBuM!")]
    Kabum = 37,

    [Description("Americanas")]
    Americanas = 38,

    [Description("Submarino")]
    Submarino = 39,

    [Description("Extra")]
    Extra = 40,

    [Description("Carrefour")]
    Carrefour = 41,

    [Description("Havan")]
    Havan = 42,

    [Description("Shein")]
    Shein = 43,

    // 🍔 Apps de delivery
    [Description("iFood")]
    Ifood = 60,

    [Description("Rappi")]
    Rappi = 61,

    [Description("Uber Eats")]
    UberEats = 62,

    [Description("Zé Delivery")]
    ZeDelivery = 63,

    [Description("AiQFome")]
    Aiqfome = 64,

    [Description("James Delivery")]
    JamesDelivery = 65,

    [Description("99Food")]
    NoventaNoveFood = 66,

    [Description("Mercado")]
    Mercado = 80,

    [Description("Farmácia")]
    Farmacia = 81,

    [Description("Drogasil")]
    Drogasil = 82,

    [Description("Droga Raia")]
    DrogaRaia = 83,

    [Description("Panvel")]
    Panvel = 84,

    [Description("Pague Menos")]
    PagueMenos = 85,

    [Description("Água")]
    Agua = 100,

    [Description("Gás")]
    Gas = 101,

    [Description("Documentos")]
    Documentos = 102,

    [Description("Encomenda Particular")]
    EncomendaParticular = 103,

    // ❓ Outros
    [Description("Outros")]
    Outros = 999
}
