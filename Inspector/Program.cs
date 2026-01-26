using System;
using System.Reflection;
using System.Linq;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

Console.WriteLine("Inspecting TraderLoyaltyLevel Properties:");
var props = typeof(TraderLoyaltyLevel).GetProperties();
foreach (var p in props)
{
    Console.WriteLine($"{p.Name} ({p.PropertyType})");
}
