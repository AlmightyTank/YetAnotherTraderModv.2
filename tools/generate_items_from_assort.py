#!/usr/bin/env python3
"""
Generate Tony config/items.json from a trader data/assort.json file.

This version can use the tarkov.dev GraphQL API to resolve readable item names
from template IDs, then falls back to locale/catalog/built-in/raw ID names.

Outputs the barter-aware schema used by PriceConfigItem.cs:
[
  {
    "OfferId": "root assort item id",
    "TplId": "sold item tpl",
    "ItemName": "readable name if available",
    "Price": 42000,
    "Currency": "RUB",
    "CashOnly": true,
    "BarterScheme": [[{"TplId": "5449016a4bdc2d6f028b456f", "ItemName": "Roubles", "Count": 42000}]]
  }
]

Basic usage from your mod root:
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json

Use tarkov.dev names (default):
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json --tarkov-dev

Offline only / do not call tarkov.dev:
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json --no-tarkov-dev

Optional readable names / fallback prices:
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json --catalog config/items.old.json
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json --locale path/to/en.json

Generate missing real barter recipes for cash-only rows, using Price as the target value:
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json --catalog config/items.json --generate-barter-schemes cash-only

Ammo rows generate a real item BarterScheme valued as a whole ammo pack and, when possible,
write the matching ammo pack template so the runtime loader can sell the pack when the offer rolls barter:
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json --catalog config/items.json --generate-barter-schemes cash-only --ammo-barter-pack-size 30

Force every row to cash-only while still preserving the original/generated barter recipe in BarterScheme:
  python tools/generate_items_from_assort.py --assort data/assort.json --out config/items.json --cash-only --catalog config/items.old.json
"""

from __future__ import annotations

import argparse
import json
import random
import re
import time
import urllib.error
import urllib.request

SCRIPT_VERSION = "2.5.3"
from collections import Counter
from pathlib import Path
from typing import Any

RUB_TPL = "5449016a4bdc2d6f028b456f"
USD_TPL = "5696686a4bdc2da3298b456a"
EUR_TPL = "569668774bdc2da2298b4568"

TARKOV_DEV_GRAPHQL_URL = "https://api.tarkov.dev/graphql"
TARKOV_DEV_CACHE_MAX_AGE_SECONDS = 7 * 24 * 60 * 60

CURRENCY_BY_TPL = {
    RUB_TPL: "RUB",
    USD_TPL: "USD",
    EUR_TPL: "EUR",
}

CURRENCY_NAME_BY_TPL = {
    RUB_TPL: "Roubles",
    USD_TPL: "Dollars",
    EUR_TPL: "Euros",
}

TPL_BY_CURRENCY = {value: key for key, value in CURRENCY_BY_TPL.items()}

# Small built-in name map for common Tony assortment entries. Names are only for readability;
# TplId, OfferId, Price, Currency, CashOnly, and BarterScheme are what the mod actually uses.
BUILT_IN_NAMES = {
    "5449016a4bdc2d6f028b456f": "Roubles",
    "5696686a4bdc2da3298b456a": "Dollars",
    "569668774bdc2da2298b4568": "Euros",
    "56d59d3ad2720bdb418b4577": "9x19mm Pst gzh",
    "56dff4a2d2720bbd668b456a": "5.45x39mm T gs",
    "56dff3afd2720bba668b4567": "5.45x39mm PS gs",
    "5656d7c34bdc2d9d198b4587": "7.62x39mm PS gzh",
    "59e6542b86f77411dc52a77a": ".366 TKM FMJ",
    "560d5e524bdc2d25448b4571": "12/70 7mm buckshot",
    "5448bd6b4bdc2dfc2f8b4569": "Makarov PM 9x18PM pistol",
    "57dc2fa62459775949412633": "Kalashnikov AKS-74U 5.45x39 assault rifle",
    "54491c4f4bdc2db1078b4568": "MP-133 12ga pump-action shotgun",
    "60339954d62c9b14ed777c06": "Soyuz-TM STM-9 Gen.2 9x19 carbine",
    "5e4abfed86f77406a2713cf7": "Splav Tarzan M22 chest rig (Smog)",
    "5648a7494bdc2d9d488b4583": "PACA Soft Armor",
    "5c0e5bab86f77461f55ed1f3": "6B23-1 body armor (EMR)",
    "5c06c6a80db834001b735491": "SSh-68 steel helmet (Olive Drab)",
    "544fb25a4bdc2dfb738b4567": "Aseptic bandage",
    "5755356824597772cb798962": "AI-2 medkit",
    "544fb3364bdc2d34748b456a": "Immobilizing splint",
    "544fb37f4bdc2dee738b4567": "Analgin painkillers",
    "590c2e1186f77425357b6124": "Toolset",
    "56dff421d2720b5f5a8b4567": "5.45x39mm SP",
    "56dfef82d2720bbd668b4567": "5.45x39mm BP gs",
    "56dff061d2720bb5668b4567": "5.45x39mm BT gs",
    "59e4cf5286f7741778269d8a": "7.62x39mm T-45M1 gzh",
    "57a0dfb82459774d3078b56c": "9x39mm SP-5 gs",
    "5644bd2b4bdc2d3b4c8b4572": "Kalashnikov AK-74N 5.45x39 assault rifle",
    "5ac66d2e5acfc43b321d4b53": "Kalashnikov AK-103 7.62x39 assault rifle",
    "59d6088586f774275f37482f": "Kalashnikov AKM 7.62x39 assault rifle",
    "5ac66d9b5acfc4001633997a": "Kalashnikov AK-105 5.45x39 assault rifle",
    "59984ab886f7743e98271174": "PP-19-01 Vityaz 9x19 submachine gun",
    "59e7643b86f7742cbf2c109a": "WARTECH TV-109 + TV-106 chest rig (A-TACS FG)",
    "5d5d646386f7742797261fd9": "6B3TM-01 armored rig (Khaki)",
    "5a7c4850e899ef00150be885": "6B47 Ratnik-BSh helmet (Olive Drab)",
    "5aa7cfc0e5b5b00015693143": "6B47 Ratnik-BSh helmet (Digital Flora cover)",
    "544fb45d4bdc2dee738b4568": "Salewa first aid kit",
    "590c661e86f7741e566b646a": "Car first aid kit",
    "60098af40accd37ef2175f27": "CAT hemostatic tourniquet",
    "5e831507ea0a7c419c2f9bd9": "Esmarch tourniquet",
    "5e8488fa988a8701445df1e4": "CALOK-B hemostatic applicator",
    "5d02778e86f774203e7dedbe": "CMS surgical kit",
    "5991b51486f77447b112d44f": "MS2000 Marker",
    "57838ad32459774a17445cd2": "VSS Vintorez 9x39 special sniper rifle",
    "59fb023c86f7746d0d4b423c": "Weapon case",
    "59fb042886f7746c5005a7b2": "Item case",
}

# Extra embedded fallbacks for Tony's curated assortment. The tarkov.dev lookup is still preferred;
# these only prevent UNKNOWN_ITEM_* names when running fully offline or when the API schema/network fails.
BUILT_IN_NAMES.update({
    "57372140245977611f70ee91": "9x18mm PM SP7 gzh",
    "56d59d3ad2720bdb418b4577": "9x19mm Pst gzh",
    "56dff4a2d2720bbd668b456a": "5.45x39mm T gs",
    "56dff3afd2720bba668b4567": "5.45x39mm PS gs",
    "5656d7c34bdc2d9d198b4587": "7.62x39mm PS gzh",
    "59e6542b86f77411dc52a77a": ".366 TKM FMJ",
    "59e655cb86f77411dc52a77b": ".366 TKM EKO",
    "560d5e524bdc2d25448b4571": "12/70 7mm buckshot",
    "5448bd6b4bdc2dfc2f8b4569": "Makarov PM 9x18PM pistol",
    "57dc2fa62459775949412633": "Kalashnikov AKS-74U 5.45x39 assault rifle",
    "59e6152586f77473dc057aa1": "Molot Arms VPO-136 Vepr-KM 7.62x39 carbine",
    "59e6687d86f77411d949b251": "Molot Arms VPO-209 .366 TKM carbine",
    "54491c4f4bdc2db1078b4568": "MP-133 12ga pump-action shotgun",
    "60339954d62c9b14ed777c06": "Soyuz-TM STM-9 Gen.2 9x19 carbine",
    "5e4abfed86f77406a2713cf7": "Splav Tarzan M22 chest rig (Smog)",
    "5648a69d4bdc2ded0b8b457b": "BlackRock chest rig (Gray)",
    "5648a7494bdc2d9d488b4583": "PACA Soft Armor",
    "5c0e5bab86f77461f55ed1f3": "6B23-1 body armor (EMR)",
    "5c06c6a80db834001b735491": "SSh-68 steel helmet (Olive Drab)",
    "544fb25a4bdc2dfb738b4567": "Aseptic bandage",
    "5751a25924597722c463c472": "Army bandage",
    "5755356824597772cb798962": "AI-2 medkit",
    "544fb3364bdc2d34748b456a": "Immobilizing splint",
    "544fb37f4bdc2dee738b4567": "Analgin painkillers",
    "590c2e1186f77425357b6124": "Toolset",
    "56dff421d2720b5f5a8b4567": "5.45x39mm SP",
    "56dfef82d2720bbd668b4567": "5.45x39mm BP gs",
    "56dff061d2720bb5668b4567": "5.45x39mm BT gs",
    "59e4cf5286f7741778269d8a": "7.62x39mm T-45M1 gzh",
    "5c925fa22e221601da359b7b": "9x19mm AP 6.3",
    "57a0dfb82459774d3078b56c": "9x39mm SP-5 gs",
    "5d6e6911a4b9361bd5780d52": "12/70 flechette",
    "5644bd2b4bdc2d3b4c8b4572": "Kalashnikov AK-74N 5.45x39 assault rifle",
    "5ac66d2e5acfc43b321d4b53": "Kalashnikov AK-103 7.62x39 assault rifle",
    "59d6088586f774275f37482f": "Kalashnikov AKM 7.62x39 assault rifle",
    "5ac66d9b5acfc4001633997a": "Kalashnikov AK-105 5.45x39 assault rifle",
    "59984ab886f7743e98271174": "PP-19-01 Vityaz 9x19 submachine gun",
    "576165642459773c7a400233": "Saiga-12K ver.10 12ga semi-automatic shotgun",
    "59e7643b86f7742cbf2c109a": "WARTECH TV-109 + TV-106 chest rig (A-TACS FG)",
    "5d5d646386f7742797261fd9": "6B3TM-01 armored rig (Khaki)",
    "5a7c4850e899ef00150be885": "6B47 Ratnik-BSh helmet (Olive Drab)",
    "5aa7cfc0e5b5b00015693143": "6B47 Ratnik-BSh helmet (Digital Flora cover)",
    "544fb45d4bdc2dee738b4568": "Salewa first aid kit",
    "590c661e86f7741e566b646a": "Car first aid kit",
    "60098af40accd37ef2175f27": "CAT hemostatic tourniquet",
    "5e831507ea0a7c419c2f9bd9": "Esmarch tourniquet",
    "5e8488fa988a8701445df1e4": "CALOK-B hemostatic applicator",
    "5d02778e86f774203e7dedbe": "CMS surgical kit",
    "5910968f86f77425cf569c32": "Weapon repair kit",
    "5991b51486f77447b112d44f": "MS2000 Marker",
    "56dff026d2720bb8668b4567": "5.45x39mm BS gs",
    "59e0d99486f7744a32234762": "7.62x39mm BP gzh",
    "5efb0da7a29a85116f6ea05f": "9x19mm PBP gzh",
    "57a0e5022459774d1673f889": "9x39mm SP-6 gs",
    "5c0d668f86f7747ccb7f13b2": "9x39mm SPP gs",
    "5f0596629e22f464da6bbdd9": ".366 TKM AP-M",
    "5d6e68a8a4b9360b6c0d54e2": "12/70 AP-20 armor-piercing slug",
    "5beed0f50db834001c062b12": "RPK-16 5.45x39 light machine gun",
    "57c44b372459772d2b39b8ce": "AS VAL 9x39 special assault rifle",
    "57838ad32459774a17445cd2": "VSS Vintorez 9x39 special sniper rifle",
    "5c46fbd72e2216398b5a8c9c": "SVDS 7.62x54R sniper rifle",
    "5ab8e79e86f7742d8b372e78": "BNTI Gzhel-K body armor",
    "5aafbde786f774389d0cbc0f": "Ammunition case",
    "590c60fc86f77412b13fddcf": "Documents case",
    "59fafd4b86f7745ca07e1232": "Key tool",
    "5c0d5e4486f77478390952fe": "5.45x39mm PPBS gs Igolnik",
    "61962d879bb3d20b0946d385": "9x39mm PAB-9 gs",
    "601aa3d2b2bcb34913271e6d": "7.62x39mm MAI AP",
    "5c0d688c86f77413ae3407b2": "9x39mm BP gs",
    "628a60ae6b1d481ff772e9c8": "Rifle Dynamics RD-704 7.62x39 assault rifle",
    "5c0e625a86f7742d77340f62": "BNTI Zhuk body armor (EMR)",
    "59fb023c86f7746d0d4b423c": "Weapon case",
    "59fb042886f7746c5005a7b2": "Item case",
    "5c0a840b86f7742ffa4f2482": "T H I C C item case",
    "5a1eaa87fcdbcb001865f75e": "Trijicon REAP-IR thermal scope",
    "5d1b5e94d7ad1a2b865a96b0": "FLIR RS-32 2.25-9x 35mm 60Hz thermal riflescope",
    "5c0d56a986f774449d5de529": "9x39mm SPP gs",
    "5d02797c86f774203f38e30a": "Surv12 field surgical kit"
})

# Built-in cash fallback prices for Tony barter-only offers when no catalog/API price exists.
BUILT_IN_PRICES = {
    "57372140245977611f70ee91": 950,
    "56d59d3ad2720bdb418b4577": 1050,
    "56dff4a2d2720bbd668b456a": 950,
    "56dff3afd2720bba668b4567": 1250,
    "5656d7c34bdc2d9d198b4587": 1600,
    "59e6542b86f77411dc52a77a": 850,
    "59e655cb86f77411dc52a77b": 1150,
    "560d5e524bdc2d25448b4571": 900,
    "5448bd6b4bdc2dfc2f8b4569": 27500,
    "57dc2fa62459775949412633": 56500,
    "59e6152586f77473dc057aa1": 82500,
    "59e6687d86f77411d949b251": 89500,
    "54491c4f4bdc2db1078b4568": 36500,
    "60339954d62c9b14ed777c06": 84500,
    "5e4abfed86f77406a2713cf7": 10500,
    "5648a69d4bdc2ded0b8b457b": 24000,
    "5648a7494bdc2d9d488b4583": 16000,
    "5c0e5bab86f77461f55ed1f3": 42000,
    "5c06c6a80db834001b735491": 22000,
    "544fb25a4bdc2dfb738b4567": 2400,
    "5751a25924597722c463c472": 3600,
    "5755356824597772cb798962": 4500,
    "544fb3364bdc2d34748b456a": 2600,
    "544fb37f4bdc2dee738b4567": 6200,
    "590c2e1186f77425357b6124": 75000,
    "56dff421d2720b5f5a8b4567": 1700,
    "56dfef82d2720bbd668b4567": 2400,
    "56dff061d2720bb5668b4567": 3200,
    "59e4cf5286f7741778269d8a": 1700,
    "5c925fa22e221601da359b7b": 4100,
    "57a0dfb82459774d3078b56c": 2600,
    "5d6e6911a4b9361bd5780d52": 3500,
    "5644bd2b4bdc2d3b4c8b4572": 42000,
    "5ac66d2e5acfc43b321d4b53": 50000,
    "59d6088586f774275f37482f": 56000,
    "5ac66d9b5acfc4001633997a": 64000,
    "59984ab886f7743e98271174": 42000,
    "576165642459773c7a400233": 43000,
    "59e7643b86f7742cbf2c109a": 39000,
    "5d5d646386f7742797261fd9": 52000,
    "5a7c4850e899ef00150be885": 54000,
    "5aa7cfc0e5b5b00015693143": 59000,
    "544fb45d4bdc2dee738b4568": 17500,
    "590c661e86f7741e566b646a": 11000,
    "60098af40accd37ef2175f27": 5200,
    "5e831507ea0a7c419c2f9bd9": 5200,
    "5e8488fa988a8701445df1e4": 11200,
    "5d02778e86f774203e7dedbe": 38000,
    "5910968f86f77425cf569c32": 210000,
    "5991b51486f77447b112d44f": 260000,
    "56dff026d2720bb8668b4567": 6200,
    "59e0d99486f7744a32234762": 7500,
    "5efb0da7a29a85116f6ea05f": 6400,
    "57a0e5022459774d1673f889": 5200,
    "5c0d668f86f7747ccb7f13b2": 8500,
    "5f0596629e22f464da6bbdd9": 6800,
    "5d6e68a8a4b9360b6c0d54e2": 6200,
    "5beed0f50db834001c062b12": 95000,
    "57c44b372459772d2b39b8ce": 105000,
    "57838ad32459774a17445cd2": 118000,
    "5c46fbd72e2216398b5a8c9c": 128000,
    "5ab8e79e86f7742d8b372e78": 170000,
    "5aafbde786f774389d0cbc0f": 120000,
    "590c60fc86f77412b13fddcf": 90000,
    "59fafd4b86f7745ca07e1232": 160000,
    "5c0d5e4486f77478390952fe": 11200,
    "61962d879bb3d20b0946d385": 13200,
    "601aa3d2b2bcb34913271e6d": 15500,
    "5c0d688c86f77413ae3407b2": 15500,
    "628a60ae6b1d481ff772e9c8": 165000,
    "5c0e625a86f7742d77340f62": 90000,
    "59fb023c86f7746d0d4b423c": 250000,
    "59fb042886f7746c5005a7b2": 400000,
    "5c0a840b86f7742ffa4f2482": 4500000,
    "5a1eaa87fcdbcb001865f75e": 400000,
    "5d1b5e94d7ad1a2b865a96b0": 170000
}


# Curated fallback barter pool. Values are tuning numbers used only by this generator;
# they are not pulled from the live SPT database. Keep them close to Tony's theme:
# tools, weapon parts, alcohol, cigarettes, valuables, meds, and useful junk.
BARTER_ITEM_POOL: list[dict[str, Any]] = [
    {"TplId": "57347b8b24597737dd42e192", "ItemName": "Classic matches", "Value": 2500, "Tags": ["cheap", "utility", "food", "generic"], "MaxCount": 6},
    {"TplId": "5e2af2bc86f7746d3f3c33fc", "ItemName": "Hunting matches", "Value": 6500, "Tags": ["cheap", "utility", "weapon", "generic"], "MaxCount": 5},
    {"TplId": "57347c1124597737fb1379e3", "ItemName": "Duct tape", "Value": 7000, "Tags": ["tool", "weapon", "armor", "generic"], "MaxCount": 6},
    {"TplId": "5734795124597738002c6176", "ItemName": "Insulating tape", "Value": 4500, "Tags": ["tool", "weapon", "cheap", "generic"], "MaxCount": 6},
    {"TplId": "5e2af29386f7746d4159f077", "ItemName": "KEKTAPE duct tape", "Value": 13000, "Tags": ["tool", "weapon", "armor", "generic"], "MaxCount": 5},
    {"TplId": "590c2d8786f774245b1f03f3", "ItemName": "Screwdriver", "Value": 9000, "Tags": ["tool", "weapon", "generic"], "MaxCount": 5},
    {"TplId": "590c2b4386f77425357b6123", "ItemName": "Pliers", "Value": 11000, "Tags": ["tool", "weapon", "armor", "generic"], "MaxCount": 5},
    {"TplId": "590c31c586f774245e3141b2", "ItemName": "Pack of nails", "Value": 15000, "Tags": ["tool", "armor", "case", "generic"], "MaxCount": 5},
    {"TplId": "5d1c819a86f774771b0acd6c", "ItemName": "Weapon parts", "Value": 22000, "Tags": ["weapon", "suppressor", "case", "generic"], "MaxCount": 12},
    {"TplId": "5d6fc78386f77449d825f9dc", "ItemName": "Gunpowder \"Eagle\"", "Value": 35000, "Tags": ["ammo", "weapon", "case"], "MaxCount": 8},
    {"TplId": "5d0375ff86f774186372f685", "ItemName": "Military cable", "Value": 45000, "Tags": ["weapon", "case", "tech", "generic"], "MaxCount": 6},
    {"TplId": "5d1b2fa286f77425227d1674", "ItemName": "Electric motor", "Value": 65000, "Tags": ["tool", "case", "tech", "generic"], "MaxCount": 6},
    {"TplId": "5d03775b86f774203e7e0c4b", "ItemName": "Phased array element", "Value": 150000, "Tags": ["tech", "thermal", "case", "armor"], "MaxCount": 6},
    {"TplId": "5e2aedd986f7746d404f3aa4", "ItemName": "GreenBat lithium battery", "Value": 70000, "Tags": ["tech", "case", "generic"], "MaxCount": 6},
    {"TplId": "5d40407c86f774318526545a", "ItemName": "Bottle of Tarkovskaya vodka", "Value": 25000, "Tags": ["alcohol", "armor", "weapon", "generic"], "MaxCount": 8},
    {"TplId": "5d403f9186f7743cac3f229b", "ItemName": "Bottle of Dan Jackiel whiskey", "Value": 45000, "Tags": ["alcohol", "armor", "weapon", "generic"], "MaxCount": 6},
    {"TplId": "5d1b376e86f774252519444e", "ItemName": "Bottle of Fierce Hatchling moonshine", "Value": 220000, "Tags": ["alcohol", "valuable", "case", "thermal", "armor"], "MaxCount": 8},
    {"TplId": "5734758f24597738025ee253", "ItemName": "Golden neck chain", "Value": 35000, "Tags": ["valuable", "armor", "case", "generic"], "MaxCount": 10},
    {"TplId": "59faf7ca86f7740dbe19f6c2", "ItemName": "Roler Submariner gold wrist watch", "Value": 90000, "Tags": ["valuable", "armor", "case", "generic"], "MaxCount": 8},
    {"TplId": "5d235a5986f77443f6329bc6", "ItemName": "Gold skull ring", "Value": 65000, "Tags": ["valuable", "armor", "case", "helmet", "generic"], "MaxCount": 8},
    {"TplId": "5c1267ee86f77416ec610f72", "ItemName": "Chain with Prokill medallion", "Value": 85000, "Tags": ["valuable", "medical", "injector", "generic"], "MaxCount": 6},
    {"TplId": "590de71386f774347051a052", "ItemName": "Antique teapot", "Value": 55000, "Tags": ["valuable", "case", "generic"], "MaxCount": 6},
    {"TplId": "590c651286f7741e566b6461", "ItemName": "Slim diary", "Value": 25000, "Tags": ["intel", "case", "generic"], "MaxCount": 6},
    {"TplId": "5c12613b86f7743bbe2c3f76", "ItemName": "Intelligence folder", "Value": 300000, "Tags": ["intel", "case", "thermal", "valuable"], "MaxCount": 8},
    {"TplId": "59faff1d86f7746c51718c9c", "ItemName": "Physical Bitcoin", "Value": 600000, "Tags": ["valuable", "case", "thermal"], "MaxCount": 10},
    {"TplId": "57347d7224597744596b4e72", "ItemName": "Can of beef stew (Small)", "Value": 14000, "Tags": ["food", "cheap", "generic"], "MaxCount": 6},
    {"TplId": "575062b524597720a31c09a1", "ItemName": "Can of Ice Green tea", "Value": 12000, "Tags": ["food", "cheap", "generic"], "MaxCount": 6},
    {"TplId": "5751435d24597720a27126d1", "ItemName": "Can of Max Energy energy drink", "Value": 13000, "Tags": ["food", "medical", "generic"], "MaxCount": 6},
    {"TplId": "62a0a043cf4a99369e2624a5", "ItemName": "Bottle of OLOLO Multivitamins", "Value": 18000, "Tags": ["medical", "injector", "generic"], "MaxCount": 6},
    {"TplId": "5d1b32c186f774252167a530", "ItemName": "Analog thermometer", "Value": 25000, "Tags": ["medical", "injector", "tech"], "MaxCount": 6},
    {"TplId": "590a3d9c86f774385926e510", "ItemName": "Ultraviolet lamp", "Value": 28000, "Tags": ["medical", "tech", "injector"], "MaxCount": 6},
    {"TplId": "5734770f24597738025ee254", "ItemName": "Strike Cigarettes", "Value": 8000, "Tags": ["cigarette", "medical", "cheap", "generic"], "MaxCount": 8},
    {"TplId": "573476d324597737da2adc13", "ItemName": "Malboro Cigarettes", "Value": 9000, "Tags": ["cigarette", "armor", "cheap", "generic"], "MaxCount": 8},
    {"TplId": "5672cb124bdc2d1a0f8b4568", "ItemName": "AA Battery", "Value": 8000, "Tags": ["tech", "tool", "cheap", "generic"], "MaxCount": 8},
    {"TplId": "5e2aef7986f7746d3f3c33f5", "ItemName": "Repellent", "Value": 17000, "Tags": ["medical", "food", "cheap", "generic"], "MaxCount": 6},
]



BARTER_POOL_NAME_BY_TPL = {str(component["TplId"]): str(component["ItemName"]) for component in BARTER_ITEM_POOL}
BARTER_POOL_VALUE_BY_TPL = {str(component["TplId"]): float(component["Value"]) for component in BARTER_ITEM_POOL}

# Known ammo templates from Tony's curated assort. Used only so generated ammo barters
# are priced as a pack of rounds instead of a single round. Generic name matching
# below catches future/custom ammo rows when their names start with a caliber.
KNOWN_AMMO_TPLS: set[str] = {
    "5f0596629e22f464da6bbdd9",  # .366 TKM AP-M
    "59e655cb86f77411dc52a77b",  # .366 TKM EKO
    "59e6542b86f77411dc52a77a",  # .366 TKM FMJ
    "560d5e524bdc2d25448b4571",  # 12/70 7mm buckshot
    "5d6e68a8a4b9360b6c0d54e2",  # 12/70 AP-20
    "5d6e6911a4b9361bd5780d52",  # 12/70 flechette
    "56dfef82d2720bbd668b4567",  # 5.45 BP
    "56dff026d2720bb8668b4567",  # 5.45 BS
    "56dff061d2720bb5668b4567",  # 5.45 BT
    "56dff2ced2720bb4668b4567",  # 5.45 PP
    "5c0d5e4486f77478390952fe",  # 5.45 PPBS Igolnik
    "56dff3afd2720bba668b4567",  # 5.45 PS
    "56dff421d2720b5f5a8b4567",  # 5.45 SP
    "59e0d99486f7744a32234762",  # 7.62 BP
    "601aa3d2b2bcb34913271e6d",  # 7.62 MAI AP
    "64b7af434b75259c590fa893",  # 7.62 PP
    "5656d7c34bdc2d9d198b4587",  # 7.62 PS
    "59e4cf5286f7741778269d8a",  # 7.62 T-45M1
    "57372140245977611f70ee91",  # 9x18 SP7
    "5c925fa22e221601da359b7b",  # 9x19 AP 6.3
    "5efb0da7a29a85116f6ea05f",  # 9x19 PBP
    "56d59d3ad2720bdb418b4577",  # 9x19 Pst
    "5c0d56a986f774449d5de529",  # 9x19 RIP
    "5c0d688c86f77413ae3407b2",  # 9x39 BP
    "61962d879bb3d20b0946d385",  # 9x39 PAB-9
    "57a0dfb82459774d3078b56c",  # 9x39 SP-5
    "57a0e5022459774d1673f889",  # 9x39 SP-6
    "5c0d668f86f7747ccb7f13b2",  # 9x39 SPP
}

AMMO_NAME_START_RE = re.compile(
    r"^(?:\.366|\.300|\.338|\.357|\.45|4\.6x30|5\.45x39|5\.56x45|7\.62x25|7\.62x39|7\.62x51|7\.62x54|9x18|9x19|9x21|9x39|12/70|20/70|12\.7x55)\b",
    re.IGNORECASE,
)

AMMO_PACK_SIZE_RE = re.compile(r"\((\d+)\s*pcs?\)", re.IGNORECASE)
AMMO_PACK_NAME_RE = re.compile(r"\s+ammo\s+pack\s*\(\d+\s*pcs?\).*?$", re.IGNORECASE)

# Offline fallback pack templates for common Tony ammo rows. The generator still prefers
# tarkov.dev or locale/catalog name discovery when available. If a row is not here and no
# pack can be discovered, that ammo row will remain cash-only for runtime randomization.
BUILT_IN_AMMO_PACKS: dict[str, dict[str, Any]] = {
    # Tony fallback ammo pack targets. These keep offline generation working when
    # tarkov.dev/cache/locales are not available. The generator still prefers
    # discovered pack templates when it can find them by name.
    "5f0596629e22f464da6bbdd9": {"AmmoBarterPackTplId": "657023f81419851aef03e6f1", "AmmoBarterPackItemName": ".366 TKM AP-M ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "59e655cb86f77411dc52a77b": {"AmmoBarterPackTplId": "657024011419851aef03e6f4", "AmmoBarterPackItemName": ".366 TKM EKO ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "560d5e524bdc2d25448b4571": {"AmmoBarterPackTplId": "657024361419851aef03e6fa", "AmmoBarterPackItemName": "12/70 7mm buckshot ammo pack (25 pcs)", "AmmoBarterPackSize": 25},
    "5d6e68a8a4b9360b6c0d54e2": {"AmmoBarterPackTplId": "64898838d5b4df6140000a20", "AmmoBarterPackItemName": "12/70 AP-20 ammo pack (25 pcs)", "AmmoBarterPackSize": 25},
    "5d6e6911a4b9361bd5780d52": {"AmmoBarterPackTplId": "65702474bfc87b3a34093226", "AmmoBarterPackItemName": "12/70 flechette ammo pack (25 pcs)", "AmmoBarterPackSize": 25},
    "56dfef82d2720bbd668b4567": {"AmmoBarterPackTplId": "57372ac324597767001bc261", "AmmoBarterPackItemName": "5.45x39mm BP gs ammo pack (30 pcs)", "AmmoBarterPackSize": 30},
    "56dff026d2720bb8668b4567": {"AmmoBarterPackTplId": "57372bd3245977670b7cd243", "AmmoBarterPackItemName": "5.45x39mm BS gs ammo pack (30 pcs)", "AmmoBarterPackSize": 30},
    "56dff061d2720bb5668b4567": {"AmmoBarterPackTplId": "57372c89245977685d4159b1", "AmmoBarterPackItemName": "5.45x39mm BT gs ammo pack (30 pcs)", "AmmoBarterPackSize": 30},
    "56dff2ced2720bb4668b4567": {"AmmoBarterPackTplId": "57372db0245977685d4159b2", "AmmoBarterPackItemName": "5.45x39mm PP gs ammo pack (30 pcs)", "AmmoBarterPackSize": 30},
    "5c0d5e4486f77478390952fe": {"AmmoBarterPackTplId": "5c1262a286f7743f8a69aab2", "AmmoBarterPackItemName": "5.45x39mm PPBS gs Igolnik ammo pack (30 pcs)", "AmmoBarterPackSize": 30},
    "56dff3afd2720bba668b4567": {"AmmoBarterPackTplId": "57372ebf2459776862260582", "AmmoBarterPackItemName": "5.45x39mm PS gs ammo pack (30 pcs)", "AmmoBarterPackSize": 30},
    "59e0d99486f7744a32234762": {"AmmoBarterPackTplId": "64acea16c4eda9354b0226b0", "AmmoBarterPackItemName": "7.62x39mm BP gzh ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "601aa3d2b2bcb34913271e6d": {"AmmoBarterPackTplId": "6489851fc827d4637f01791b", "AmmoBarterPackItemName": "7.62x39mm MAI AP ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "64b7af434b75259c590fa893": {"AmmoBarterPackTplId": "64ace9f9c4eda9354b0226aa", "AmmoBarterPackItemName": "7.62x39mm PP gzh ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "5656d7c34bdc2d9d198b4587": {"AmmoBarterPackTplId": "5649ed104bdc2d3d1c8b458b", "AmmoBarterPackItemName": "7.62x39mm PS gzh ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "57372140245977611f70ee91": {"AmmoBarterPackTplId": "573728cc24597765cc785b5d", "AmmoBarterPackItemName": "9x18mm PM SP7 gzh ammo pack (16 pcs)", "AmmoBarterPackSize": 16},
    "5c925fa22e221601da359b7b": {"AmmoBarterPackTplId": "65702591c5d7d4cb4d07857c", "AmmoBarterPackItemName": "9x19mm AP 6.3 ammo pack (50 pcs)", "AmmoBarterPackSize": 50},
    "5efb0da7a29a85116f6ea05f": {"AmmoBarterPackTplId": "648987d673c462723909a151", "AmmoBarterPackItemName": "9x19mm PBP ammo pack (50 pcs)", "AmmoBarterPackSize": 50},
    "56d59d3ad2720bdb418b4577": {"AmmoBarterPackTplId": "5739d41224597779c3645501", "AmmoBarterPackItemName": "9x19mm Pst gzh ammo pack (16 pcs)", "AmmoBarterPackSize": 16},
    "5c0d56a986f774449d5de529": {"AmmoBarterPackTplId": "5c1127bdd174af44217ab8b9", "AmmoBarterPackItemName": "9x19mm RIP ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "5c0d688c86f77413ae3407b2": {"AmmoBarterPackTplId": "6489854673c462723909a14e", "AmmoBarterPackItemName": "9x39mm BP ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "61962d879bb3d20b0946d385": {"AmmoBarterPackTplId": "657025cfbfc87b3a34093253", "AmmoBarterPackItemName": "9x39mm PAB-9 gs ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "57a0dfb82459774d3078b56c": {"AmmoBarterPackTplId": "657025d4c5d7d4cb4d078585", "AmmoBarterPackItemName": "9x39mm SP-5 gs ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "57a0e5022459774d1673f889": {"AmmoBarterPackTplId": "657025dabfc87b3a34093256", "AmmoBarterPackItemName": "9x39mm SP-6 gs ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
    "5c0d668f86f7747ccb7f13b2": {"AmmoBarterPackTplId": "657025dfcfc010a0f5006a3b", "AmmoBarterPackItemName": "9x39mm SPP gs ammo pack (20 pcs)", "AmmoBarterPackSize": 20},
}

WEAPON_NAME_HINTS = (
    "assault rifle", "sniper rifle", "bolt-action", "carbine", "shotgun",
    "submachine gun", "light machine gun", "pistol", "suppressor", "handguard",
    "muzzle", "gas tube", "helmet", "armor", "rig", "grenade",
)


def strip_json_comments_and_trailing_commas(text: str) -> str:
    """Simple fallback for JSON files with comments/trailing commas."""
    text = text.lstrip("\ufeff")
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    text = re.sub(r"(^|\s)//.*?$", r"\1", text, flags=re.MULTILINE)
    text = re.sub(r",\s*([}\]])", r"\1", text)
    return text


def load_json(path: Path) -> Any:
    text = path.read_text(encoding="utf-8")
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return json.loads(strip_json_comments_and_trailing_commas(text))


def get_ci(obj: dict[str, Any], *keys: str, default: Any = None) -> Any:
    """Case-insensitive-ish dictionary getter for SPT C# and raw JSON shapes."""
    if not isinstance(obj, dict):
        return default

    for key in keys:
        if key in obj:
            return obj[key]

    lower_map = {str(k).lower(): k for k in obj.keys()}
    for key in keys:
        actual_key = lower_map.get(key.lower())
        if actual_key is not None:
            return obj[actual_key]

    return default


def get_item_id(item: dict[str, Any]) -> str:
    return str(get_ci(item, "_id", "Id", "id", default=""))


def get_item_tpl(item: dict[str, Any]) -> str:
    return str(get_ci(item, "_tpl", "Template", "Tpl", "TemplateId", default=""))


def get_parent_id(item: dict[str, Any]) -> str:
    return str(get_ci(item, "parentId", "ParentId", "parent_id", default=""))


def get_slot_id(item: dict[str, Any]) -> str:
    return str(get_ci(item, "slotId", "SlotId", "slot_id", default=""))


def get_payment_tpl(payment: dict[str, Any]) -> str:
    return str(get_ci(payment, "_tpl", "Template", "Tpl", "TemplateId", default=""))


def get_payment_count(payment: dict[str, Any]) -> float:
    value = get_ci(payment, "count", "Count", default=0)
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def clean_number(value: float) -> int | float:
    return int(value) if float(value).is_integer() else value


def load_name_and_price_catalog(path: Path | None) -> tuple[dict[str, str], dict[str, float]]:
    names: dict[str, str] = {}
    prices: dict[str, float] = {}

    if path is None or not path.exists():
        return names, prices

    data = load_json(path)
    if not isinstance(data, list):
        return names, prices

    for row in data:
        if not isinstance(row, dict):
            continue

        tpl = str(get_ci(row, "TplId", "tplId", "_tpl", "Template", default=""))
        if not tpl:
            continue

        name = get_ci(row, "ItemName", "itemName", "Name", default=None)
        if name is not None and tpl not in names:
            names[tpl] = str(name)

        price = get_ci(row, "Price", "price", default=None)
        if price is not None and tpl not in prices:
            try:
                prices[tpl] = float(price)
            except (TypeError, ValueError):
                pass

    return names, prices


def load_locale_names(path: Path | None) -> dict[str, str]:
    names: dict[str, str] = {}

    if path is None or not path.exists():
        return names

    data = load_json(path)

    # Some SPT locale exports are {"Value": {...}}.
    value_data = get_ci(data, "Value", "value", default=None) if isinstance(data, dict) else None
    if isinstance(value_data, dict):
        data = value_data

    if not isinstance(data, dict):
        return names

    for key, value in data.items():
        key_str = str(key)

        # Standard locale key: "<tpl> Name": "Readable item name"
        if key_str.endswith(" Name"):
            tpl = key_str[:-5]
            names[tpl] = str(value)
            continue

        # Alternate/nested shape: "<tpl>": {"Name": "Readable item name"}
        if isinstance(value, dict):
            nested_name = get_ci(value, "Name", "name", default=None)
            if nested_name is not None:
                names[key_str] = str(nested_name)

    return names


def load_tarkov_dev_cache(cache_path: Path, max_age_seconds: int) -> tuple[dict[str, str], dict[str, float], list[str]]:
    warnings: list[str] = []
    names: dict[str, str] = {}
    prices: dict[str, float] = {}

    if not cache_path.exists():
        return names, prices, warnings

    try:
        cache = load_json(cache_path)
    except Exception as ex:
        warnings.append(f"Could not read tarkov.dev cache {cache_path}: {ex}")
        return names, prices, warnings

    if not isinstance(cache, dict):
        warnings.append(f"Ignoring tarkov.dev cache {cache_path}: expected JSON object")
        return names, prices, warnings

    fetched_at = float(cache.get("fetchedAt", 0) or 0)
    age = time.time() - fetched_at
    if age > max_age_seconds:
        warnings.append(f"tarkov.dev cache is older than {max_age_seconds} seconds; refreshing if API is reachable")
        return names, prices, warnings

    raw_items = cache.get("items", [])
    if not isinstance(raw_items, list):
        warnings.append(f"Ignoring tarkov.dev cache {cache_path}: items was not a list")
        return names, prices, warnings

    names, prices = parse_tarkov_dev_items(raw_items)
    warnings.append(f"Loaded {len(names)} names from tarkov.dev cache: {cache_path}")
    return names, prices, warnings


def parse_tarkov_dev_items(raw_items: list[Any]) -> tuple[dict[str, str], dict[str, float]]:
    names: dict[str, str] = {}
    prices: dict[str, float] = {}

    for item in raw_items:
        if not isinstance(item, dict):
            continue

        item_id = str(item.get("id") or "")
        if not item_id:
            continue

        name = item.get("name") or item.get("shortName")
        if name:
            names[item_id] = str(name)

        # basePrice is stable game data. avg24hPrice can be useful if basePrice is missing.
        price = item.get("basePrice")
        if price is None:
            price = item.get("avg24hPrice")
        if price is not None:
            try:
                prices[item_id] = float(price)
            except (TypeError, ValueError):
                pass

    return names, prices


def post_tarkov_dev_query(query: str, timeout_seconds: float) -> dict[str, Any]:
    payload = json.dumps({"query": query}).encode("utf-8")
    request = urllib.request.Request(
        TARKOV_DEV_GRAPHQL_URL,
        data=payload,
        method="POST",
        headers={
            "Accept": "application/json",
            "Content-Type": "application/json",
            "User-Agent": "TonyTraderItemsGenerator/1.1 (+local modding script)",
        },
    )

    with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
        response_text = response.read().decode("utf-8")

    parsed = json.loads(response_text)
    if not isinstance(parsed, dict):
        raise RuntimeError("tarkov.dev returned a non-object response")
    return parsed


def fetch_tarkov_dev_items(timeout_seconds: float) -> list[dict[str, Any]]:
    # Current tarkov.dev docs/readme examples use `items { id name shortName ... }`.
    # Some older examples used `items(lang: en)`, so keep that as a fallback.
    queries = [
        """
        query TonyItemNames {
          items {
            id
            name
            shortName
            basePrice
            avg24hPrice
          }
        }
        """,
        """
        query TonyItemNames {
          items(lang: en) {
            id
            name
            shortName
            basePrice
            avg24hPrice
          }
        }
        """,
    ]

    last_error: Any = None
    for query in queries:
        parsed = post_tarkov_dev_query(query, timeout_seconds)

        errors = parsed.get("errors")
        if errors:
            last_error = errors
            continue

        data = parsed.get("data")
        if isinstance(data, dict) and isinstance(data.get("items"), list):
            return data["items"]

        last_error = "response did not contain data.items"

    raise RuntimeError(f"tarkov.dev GraphQL lookup failed: {last_error}")


def get_tarkov_dev_names_and_prices(
    enabled: bool,
    cache_path: Path | None,
    refresh_cache: bool,
    timeout_seconds: float,
) -> tuple[dict[str, str], dict[str, float], list[str]]:
    warnings: list[str] = []
    if not enabled:
        warnings.append("tarkov.dev lookup disabled")
        return {}, {}, warnings

    if cache_path is not None and not refresh_cache:
        cached_names, cached_prices, cache_warnings = load_tarkov_dev_cache(
            cache_path,
            TARKOV_DEV_CACHE_MAX_AGE_SECONDS,
        )
        warnings.extend(cache_warnings)
        if cached_names:
            return cached_names, cached_prices, warnings

    try:
        raw_items = fetch_tarkov_dev_items(timeout_seconds)
        names, prices = parse_tarkov_dev_items(raw_items)
        warnings.append(f"Fetched {len(names)} names from tarkov.dev")

        if cache_path is not None:
            cache_path.parent.mkdir(parents=True, exist_ok=True)
            cache_payload = {
                "fetchedAt": time.time(),
                "source": TARKOV_DEV_GRAPHQL_URL,
                "items": raw_items,
            }
            cache_path.write_text(json.dumps(cache_payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
            warnings.append(f"Saved tarkov.dev cache: {cache_path}")

        return names, prices, warnings
    except (urllib.error.URLError, TimeoutError, RuntimeError, json.JSONDecodeError, OSError) as ex:
        warnings.append(f"tarkov.dev lookup failed: {ex}")

        if cache_path is not None:
            # Last-resort cache load, even if it is stale.
            try:
                cache = load_json(cache_path)
                raw_items = cache.get("items", []) if isinstance(cache, dict) else []
                if isinstance(raw_items, list):
                    names, prices = parse_tarkov_dev_items(raw_items)
                    if names:
                        warnings.append(f"Using stale tarkov.dev cache: {cache_path}")
                        return names, prices, warnings
            except Exception as cache_ex:
                warnings.append(f"Stale tarkov.dev cache also failed: {cache_ex}")

        return {}, {}, warnings


def resolve_name(
    tpl: str,
    tarkov_dev_names: dict[str, str],
    locale_names: dict[str, str],
    catalog_names: dict[str, str],
) -> str:
    if tpl in CURRENCY_NAME_BY_TPL:
        return CURRENCY_NAME_BY_TPL[tpl]
    if tpl in tarkov_dev_names:
        return tarkov_dev_names[tpl]
    if tpl in locale_names:
        return locale_names[tpl]
    if tpl in catalog_names:
        return catalog_names[tpl]
    if tpl in BARTER_POOL_NAME_BY_TPL:
        return BARTER_POOL_NAME_BY_TPL[tpl]
    if tpl in BUILT_IN_NAMES:
        return BUILT_IN_NAMES[tpl]
    return f"UNKNOWN_ITEM_{tpl}"


def normalize_barter_scheme(
    raw_scheme: Any,
    tarkov_dev_names: dict[str, str],
    locale_names: dict[str, str],
    catalog_names: dict[str, str],
) -> list[list[dict[str, Any]]]:
    normalized: list[list[dict[str, Any]]] = []

    if not isinstance(raw_scheme, list):
        return normalized

    for raw_option in raw_scheme:
        if not isinstance(raw_option, list):
            continue

        option: list[dict[str, Any]] = []
        for raw_payment in raw_option:
            if not isinstance(raw_payment, dict):
                continue

            payment_tpl = get_payment_tpl(raw_payment)
            if not payment_tpl:
                continue

            option.append(
                {
                    "TplId": payment_tpl,
                    "ItemName": resolve_name(payment_tpl, tarkov_dev_names, locale_names, catalog_names),
                    "Count": clean_number(get_payment_count(raw_payment)),
                }
            )

        if option:
            normalized.append(option)

    return normalized


def is_currency_tpl(tpl: str) -> bool:
    return tpl in CURRENCY_BY_TPL


def is_cash_only_scheme(scheme: list[list[dict[str, Any]]]) -> bool:
    if not scheme:
        return False
    return all(len(option) == 1 and is_currency_tpl(str(option[0].get("TplId", ""))) for option in scheme)


def find_primary_cash_payment(scheme: list[list[dict[str, Any]]]) -> tuple[float, str] | None:
    # Prefer simple cash-only option.
    for option in scheme:
        if len(option) == 1 and is_currency_tpl(str(option[0].get("TplId", ""))):
            return float(option[0].get("Count", 0)), CURRENCY_BY_TPL[str(option[0].get("TplId"))]

    # Otherwise find the first cash component in a mixed barter.
    for option in scheme:
        for payment in option:
            tpl = str(payment.get("TplId", ""))
            if is_currency_tpl(tpl):
                return float(payment.get("Count", 0)), CURRENCY_BY_TPL[tpl]

    return None


def choose_fallback_price(
    sold_tpl: str,
    catalog_prices: dict[str, float],
    tarkov_dev_prices: dict[str, float],
    default_price: float,
) -> float:
    # Preserve user/config catalog prices first, because these are tuned for Tony.
    if sold_tpl in catalog_prices:
        return catalog_prices[sold_tpl]
    if sold_tpl in tarkov_dev_prices:
        return tarkov_dev_prices[sold_tpl]
    if sold_tpl in BUILT_IN_PRICES:
        return float(BUILT_IN_PRICES[sold_tpl])
    return default_price


def is_real_barter_scheme(scheme: list[list[dict[str, Any]]]) -> bool:
    """True when at least one payment option requires a non-currency item."""
    return any(
        any(not is_currency_tpl(str(payment.get("TplId", ""))) for payment in option)
        for option in scheme
    )


def is_ammo_offer(item_name: str, sold_tpl: str) -> bool:
    """True for ammo rows. Avoids weapon names that merely include a caliber."""
    if sold_tpl in KNOWN_AMMO_TPLS:
        return True

    normalized_name = (item_name or "").strip().lower()
    if not normalized_name:
        return False

    if any(hint in normalized_name for hint in WEAPON_NAME_HINTS):
        return False

    return AMMO_NAME_START_RE.search(normalized_name) is not None


def ammo_pack_size_for_offer(item_name: str, sold_tpl: str, default_pack_size: int) -> int:
    """Return the barter pack size for ammo rows. Non-ammo rows return 1."""
    if not is_ammo_offer(item_name, sold_tpl):
        return 1

    # Keep this user-tunable. A default of 30 fits most rifle/SMG ammo and
    # gives Tony barters a clean "one pack" feel.
    return max(1, int(default_pack_size or 30))


def parse_ammo_pack_size(item_name: str, default_pack_size: int) -> int:
    match = AMMO_PACK_SIZE_RE.search(item_name or "")
    if match:
        try:
            return max(1, int(match.group(1)))
        except (TypeError, ValueError):
            pass
    return max(1, int(default_pack_size or 30))


def normalize_ammo_pack_match_name(item_name: str) -> str:
    """Normalize loose ammo and ammo pack names to the same comparable base."""
    name = (item_name or "").lower().strip()
    name = AMMO_PACK_NAME_RE.sub("", name)
    name = AMMO_PACK_SIZE_RE.sub("", name)
    name = name.replace("ammo pack", "")
    name = re.sub(r"\s+", " ", name)
    return name.strip(" -")


def find_ammo_pack_for_offer(
    item_name: str,
    sold_tpl: str,
    tarkov_dev_names: dict[str, str],
    locale_names: dict[str, str],
    catalog_names: dict[str, str],
    default_pack_size: int,
) -> dict[str, Any] | None:
    """Find the matching ammo pack template for a loose ammo offer.

    The output row keeps TplId as the loose bullet template for cash pricing, but
    also stores AmmoBarterPackTplId so the runtime loader can swap the sold item
    to the pack template whenever that offer rolls barter.
    """
    if not is_ammo_offer(item_name, sold_tpl):
        return None

    if sold_tpl in BUILT_IN_AMMO_PACKS:
        fallback = dict(BUILT_IN_AMMO_PACKS[sold_tpl])
        fallback.setdefault("AmmoBarterPackSize", max(1, int(default_pack_size or 30)))
        return fallback

    target_name = normalize_ammo_pack_match_name(item_name)
    if not target_name:
        return None

    # Merge all known name sources. Later entries should not overwrite earlier
    # tarkov.dev results, because those are usually the cleanest current names.
    all_names: dict[str, str] = {}
    for source in (tarkov_dev_names, locale_names, catalog_names, BUILT_IN_NAMES):
        for tpl, name in source.items():
            all_names.setdefault(str(tpl), str(name))

    candidates: list[tuple[int, int, str, str]] = []
    for tpl, candidate_name in all_names.items():
        candidate_lower = candidate_name.lower()
        if "ammo pack" not in candidate_lower:
            continue

        candidate_base = normalize_ammo_pack_match_name(candidate_name)
        if not candidate_base:
            continue

        if candidate_base == target_name:
            score = 0
        elif candidate_base.startswith(target_name) or target_name.startswith(candidate_base):
            score = 1
        else:
            continue

        pack_size = parse_ammo_pack_size(candidate_name, default_pack_size)
        # Prefer exact name matches, then packs close to the default size.
        size_distance = abs(pack_size - max(1, int(default_pack_size or 30)))
        candidates.append((score, size_distance, tpl, candidate_name))

    if not candidates:
        return None

    candidates.sort(key=lambda row: (row[0], row[1], row[3].lower()))
    _, _, pack_tpl, pack_name = candidates[0]
    return {
        "AmmoBarterPackTplId": pack_tpl,
        "AmmoBarterPackItemName": pack_name,
        "AmmoBarterPackSize": parse_ammo_pack_size(pack_name, default_pack_size),
    }


def infer_barter_tags(item_name: str, sold_tpl: str) -> list[str]:
    name = item_name.lower()
    tags = {"generic"}

    if any(token in name for token in ("ammo", "x39", "x19", "x18", "12/70", ".366", "9x39", "7.62", "5.45")):
        tags.update({"ammo", "weapon"})
    if any(token in name for token in ("ak", "vss", "val", "rpk", "vityaz", "saiga", "shotgun", "rifle", "carbine", "suppressor", "muzzle", "handguard", "gas tube")):
        tags.update({"weapon"})
    if any(token in name for token in ("suppressor", "pbs", "silencer")):
        tags.update({"suppressor", "weapon"})
    if any(token in name for token in ("armor", "armored", "helmet", "mask", "rig", "paca", "gzhel", "zhuk", "6b")):
        tags.update({"armor"})
    if "helmet" in name:
        tags.update({"helmet", "armor"})
    if any(token in name for token in ("med", "bandage", "tourniquet", "splint", "surgical", "salewa", "cms", "surv", "injector", "stim", "painkiller")):
        tags.update({"medical"})
    if any(token in name for token in ("injector", "stimulant", "propital", "m.u.l.e", "perfotoran", "sj12", "adrenaline")):
        tags.update({"injector", "medical"})
    if any(token in name for token in ("case", "documents", "key tool")):
        tags.update({"case", "valuable"})
    if any(token in name for token in ("thermal", "flir", "reap-ir")):
        tags.update({"thermal", "tech", "valuable"})
    if any(token in name for token in ("grenade", "vog", "rgd", "rgo", "rgn", "f-1")):
        tags.update({"weapon", "ammo"})
    if any(token in name for token in ("water", "ration", "stew", "aquamari")):
        tags.update({"food"})
    if any(token in name for token in ("backpack", "bag")):
        tags.update({"generic", "tool"})

    return sorted(tags)


def choose_barter_pool_for_offer(item_name: str, sold_tpl: str, price: float) -> list[dict[str, Any]]:
    wanted_tags = set(infer_barter_tags(item_name, sold_tpl))

    # Cheap goods should not ask for bitcoins or rare tech.
    if price < 10_000:
        allowed_value = 20_000
    elif price < 50_000:
        allowed_value = 90_000
    elif price < 200_000:
        allowed_value = 250_000
    elif price < 1_000_000:
        allowed_value = 650_000
    else:
        allowed_value = 1_000_000

    candidates = []
    for component in BARTER_ITEM_POOL:
        if component["TplId"] == sold_tpl:
            continue
        component_tags = set(component.get("Tags", []))
        component_value = float(component.get("Value", 0) or 0)
        if component_value <= 0 or component_value > allowed_value:
            continue
        if component_tags & wanted_tags or "generic" in component_tags:
            candidates.append(component)

    if candidates:
        return candidates

    return [component for component in BARTER_ITEM_POOL if component["TplId"] != sold_tpl]


def make_payment_component(component: dict[str, Any], count: int) -> dict[str, Any]:
    return {
        "TplId": str(component["TplId"]),
        "ItemName": str(component["ItemName"]),
        "Count": int(max(1, count)),
    }


def make_cash_payment_scheme(price: float, currency: str) -> list[list[dict[str, Any]]]:
    currency_code = (currency or "RUB").upper()
    currency_tpl = TPL_BY_CURRENCY.get(currency_code, RUB_TPL)
    return [[
        {
            "TplId": currency_tpl,
            "ItemName": CURRENCY_NAME_BY_TPL.get(currency_tpl, currency_code),
            "Count": clean_number(round(float(price or 0))),
        }
    ]]


def calculate_ammo_pack_price(price: float, pack_count: int, value_multiplier: float) -> float:
    pack_count = max(1, int(pack_count or 1))
    multiplier = max(float(value_multiplier or 1.0), 0.01)
    return round(float(price or 0) * pack_count * multiplier)


def generate_barter_scheme_for_price(
    sold_tpl: str,
    item_name: str,
    price: float,
    value_multiplier: float,
    max_components: int,
    rng: random.Random,
    pack_count: int = 1,
) -> list[list[dict[str, Any]]]:
    """Create one plausible non-currency barter option from the item cash Price.

    For ammo, pack_count should be greater than 1 so the generated barter
    represents buying a pack of rounds, not paying a barter item for one round.
    """
    pack_count = max(1, int(pack_count or 1))
    target = max(float(price or 0) * pack_count * max(value_multiplier, 0.01), 1.0)
    max_components = max(1, min(int(max_components), 6))

    if target < 10_000:
        component_slots = 1
    elif target < 50_000:
        component_slots = rng.randint(1, min(2, max_components))
    elif target < 200_000:
        component_slots = rng.randint(2, min(3, max_components))
    else:
        component_slots = rng.randint(3, max_components)

    pool = choose_barter_pool_for_offer(item_name, sold_tpl, target)
    pool = sorted(pool, key=lambda component: float(component.get("Value", 0) or 0))

    selected: list[dict[str, Any]] = []
    remaining = target
    used_tpls: set[str] = set()

    for slot_index in range(component_slots):
        slots_left = component_slots - slot_index
        ideal_piece_value = max(remaining / max(slots_left, 1), 1.0)

        # Keep candidates near the current target piece. Add a little randomness so
        # repeated runs with different seeds do not all look identical.
        available = [component for component in pool if component["TplId"] not in used_tpls]
        near = [
            component for component in available
            if float(component.get("Value", 0) or 0) <= max(ideal_piece_value * 1.75, 5_000)
        ]
        if not near:
            near = available or pool

        ranked = sorted(
            near,
            key=lambda component: abs(float(component.get("Value", 0) or 0) - ideal_piece_value),
        )
        sample_count = min(len(ranked), 5)
        component = rng.choice(ranked[:sample_count]) if sample_count else rng.choice(pool)
        component_value = max(float(component.get("Value", 1) or 1), 1.0)

        count = max(1, round(ideal_piece_value / component_value))
        count = min(count, int(component.get("MaxCount", 8) or 8))

        selected.append(make_payment_component(component, count))
        used_tpls.add(str(component["TplId"]))
        remaining -= component_value * count

    # If the result undershoots hard, increase the last stack count when possible.
    total_value = sum(
        BARTER_POOL_VALUE_BY_TPL.get(str(payment["TplId"]), 0.0) * int(payment["Count"])
        for payment in selected
    )
    if selected and total_value < target * 0.65:
        last = selected[-1]
        component = next((component for component in BARTER_ITEM_POOL if component["TplId"] == last["TplId"]), None)
        if component is not None:
            component_value = max(float(component.get("Value", 1) or 1), 1.0)
            needed_extra = max(0, round((target - total_value) / component_value))
            max_count = int(component.get("MaxCount", 8) or 8)
            last["Count"] = min(max_count, int(last["Count"]) + needed_extra)

    return [selected]


def calculate_barter_scheme_value(scheme: list[list[dict[str, Any]]]) -> float:
    """Approximate generated barter value using this script's barter pool tuning values."""
    if not scheme:
        return 0.0

    # Each outer option is an alternate price. For validation, use the cheapest valid option.
    option_values: list[float] = []
    for option in scheme:
        total = 0.0
        for payment in option:
            tpl = str(payment.get("TplId", ""))
            try:
                count = float(payment.get("Count", 0) or 0)
            except (TypeError, ValueError):
                count = 0.0
            total += BARTER_POOL_VALUE_BY_TPL.get(tpl, 0.0) * count
        if total > 0:
            option_values.append(total)

    return min(option_values) if option_values else 0.0


def generate_barter_scheme_covering_target(
    sold_tpl: str,
    item_name: str,
    target_value: float,
    max_components: int,
    rng: random.Random,
) -> list[list[dict[str, Any]]]:
    """Create a barter option that actually lands near the requested value.

    This is stricter than the generic generator and is used for ammo pack barter
    rows. It prevents bad results such as asking for one tape for a 36k ammo pack.
    """
    target = max(float(target_value or 0), 1.0)
    max_components = max(1, min(int(max_components or 1), 6))
    pool = choose_barter_pool_for_offer(item_name, sold_tpl, target)

    bundles: list[dict[str, Any]] = []
    for component in pool:
        value = float(component.get("Value", 0) or 0)
        if value <= 0:
            continue

        max_count = int(component.get("MaxCount", 8) or 8)
        # Allow enough count to reach the target, but do not make silly huge stacks.
        max_count_for_target = max(1, min(max_count, int((target * 1.35 + value - 1) // value)))

        for count in range(1, max_count_for_target + 1):
            total = value * count
            # Keep reasonable overpays, but allow one high-value item when no better item exists.
            if total > target * 1.55 and count > 1:
                continue
            bundles.append(
                {
                    "component": component,
                    "count": count,
                    "value": total,
                    "tpl": str(component["TplId"]),
                }
            )

    if not bundles:
        return generate_barter_scheme_for_price(
            sold_tpl=sold_tpl,
            item_name=item_name,
            price=target,
            value_multiplier=1.0,
            max_components=max_components,
            rng=rng,
            pack_count=1,
        )

    def score_bundle_set(bundle_set: list[dict[str, Any]]) -> tuple[float, float, int, float]:
        total = sum(float(bundle["value"]) for bundle in bundle_set)
        distance = abs(total - target)

        # Strongly punish underpaying by more than 15%.
        if total < target * 0.85:
            distance += target * 2.0

        # Mildly punish overpaying by more than 35%, but sometimes it is unavoidable.
        if total > target * 1.35:
            distance += (total - target * 1.35) * 0.75

        component_type_count = len(bundle_set)
        total_item_count = sum(int(bundle["count"]) for bundle in bundle_set)
        return (distance, abs(total - target), component_type_count, total_item_count)

    # Search a trimmed list of good bundle choices. This gives stable, near-value
    # results without exploding runtime for large assortments.
    bundles.sort(key=lambda bundle: score_bundle_set([bundle]))
    search_space = bundles[:36]

    best: list[dict[str, Any]] | None = None
    best_score: tuple[float, float, int, float] | None = None

    def consider(candidate: list[dict[str, Any]]) -> None:
        nonlocal best, best_score
        if not candidate:
            return
        tpl_list = [str(bundle["tpl"]) for bundle in candidate]
        if len(set(tpl_list)) != len(tpl_list):
            return
        score = score_bundle_set(candidate)
        if best_score is None or score < best_score:
            best = list(candidate)
            best_score = score

    # Try single components first.
    for bundle in search_space:
        consider([bundle])

    # Try small mixed trades. Four component types is enough for the values Tony sells.
    import itertools
    max_combo_size = min(max_components, 4)
    for combo_size in range(2, max_combo_size + 1):
        for combo in itertools.combinations(search_space, combo_size):
            consider(list(combo))

    if not best:
        best = [search_space[0]]

    # Randomize ordering only; keep the selected value-correct recipe.
    best = list(best)
    rng.shuffle(best)

    return [[make_payment_component(bundle["component"], int(bundle["count"])) for bundle in best]]


def should_generate_barter_scheme(mode: str, scheme: list[list[dict[str, Any]]]) -> bool:
    mode = (mode or "none").lower()
    if mode == "none":
        return False
    if mode == "all":
        return True
    if mode == "cash-only":
        return is_cash_only_scheme(scheme)
    if mode == "missing":
        return not is_real_barter_scheme(scheme)
    raise ValueError(f"unknown generate barter mode: {mode}")


def generate_items(
    assort: dict[str, Any],
    tarkov_dev_names: dict[str, str],
    tarkov_dev_prices: dict[str, float],
    locale_names: dict[str, str],
    catalog_names: dict[str, str],
    catalog_prices: dict[str, float],
    force_cash_only_rows: bool,
    default_price: float,
    generate_barter_schemes: str,
    barter_value_multiplier: float,
    barter_max_components: int,
    barter_rng: random.Random,
    ammo_barter_pack_size: int,
) -> tuple[list[dict[str, Any]], list[str]]:
    items = get_ci(assort, "items", "Items", default=[])
    barter_scheme = get_ci(assort, "barter_scheme", "BarterScheme", default={})

    if not isinstance(items, list):
        raise ValueError("assort.json does not contain an items/Items list")
    if not isinstance(barter_scheme, dict):
        raise ValueError("assort.json does not contain a barter_scheme/BarterScheme object")

    sellable_roots = []
    for item in items:
        if not isinstance(item, dict):
            continue

        parent_id = get_parent_id(item)
        slot_id = get_slot_id(item)
        if parent_id == "hideout" and (slot_id in ("", "hideout")):
            sellable_roots.append(item)

    output: list[dict[str, Any]] = []
    warnings: list[str] = []

    for root in sellable_roots:
        offer_id = get_item_id(root)
        sold_tpl = get_item_tpl(root)

        if not offer_id:
            warnings.append("Found sellable root with no id; skipped")
            continue
        if not sold_tpl:
            warnings.append(f"Offer {offer_id} has no tpl/template; skipped")
            continue

        raw_scheme = barter_scheme.get(offer_id, [])
        normalized_scheme = normalize_barter_scheme(raw_scheme, tarkov_dev_names, locale_names, catalog_names)
        cash_payment = find_primary_cash_payment(normalized_scheme)

        if cash_payment is not None:
            price, currency = cash_payment
        else:
            price = choose_fallback_price(sold_tpl, catalog_prices, tarkov_dev_prices, default_price)
            currency = "RUB"
            if not normalized_scheme:
                warnings.append(f"Offer {offer_id} / {sold_tpl} has no usable barter scheme; using fallback price {price} RUB")
            else:
                warnings.append(f"Offer {offer_id} / {sold_tpl} is barter-only; using fallback Price {price} RUB for the cash fallback field")

        item_name = resolve_name(sold_tpl, tarkov_dev_names, locale_names, catalog_names)
        original_cash_only = is_cash_only_scheme(normalized_scheme)
        is_ammo = is_ammo_offer(item_name, sold_tpl)
        ammo_pack_info = find_ammo_pack_for_offer(
            item_name=item_name,
            sold_tpl=sold_tpl,
            tarkov_dev_names=tarkov_dev_names,
            locale_names=locale_names,
            catalog_names=catalog_names,
            default_pack_size=ammo_barter_pack_size,
        )
        pack_count = int(ammo_pack_info.get("AmmoBarterPackSize", ammo_barter_pack_size)) if ammo_pack_info else ammo_pack_size_for_offer(item_name, sold_tpl, ammo_barter_pack_size)
        barter_scheme_value_basis = "Unit"

        if is_ammo and pack_count > 1:
            # Ammo is special: barter offers represent a whole ammo pack, not one loose round.
            # Generate a normal item-for-item barter with a target value of:
            # bullet Price x pack size x barter value multiplier.
            # Runtime swaps the sold template to AmmoBarterPackTplId when this offer rolls barter.
            pack_price = calculate_ammo_pack_price(float(price or 0), pack_count, barter_value_multiplier)
            generated_scheme = generate_barter_scheme_covering_target(
                sold_tpl=sold_tpl,
                item_name=item_name,
                target_value=pack_price,
                max_components=barter_max_components,
                rng=barter_rng,
            )
            if generated_scheme:
                normalized_scheme = generated_scheme

            generated_value = calculate_barter_scheme_value(normalized_scheme)
            if generated_value < pack_price * 0.80:
                warnings.append(
                    f"WARNING: Ammo barter for {item_name} ({offer_id}) is still below target: "
                    f"generated {clean_number(generated_value)} vs target {clean_number(pack_price)} {currency}"
                )
            barter_scheme_value_basis = "Pack"
            warnings.append(
                f"Generated ammo pack item-barter scheme for {item_name} ({offer_id}) "
                f"from {pack_count} rounds x {clean_number(price)} {currency} = target value {clean_number(pack_price)} {currency}"
            )
        elif should_generate_barter_scheme(generate_barter_schemes, normalized_scheme):
            generated_scheme = generate_barter_scheme_for_price(
                sold_tpl=sold_tpl,
                item_name=item_name,
                price=float(price or 0),
                value_multiplier=barter_value_multiplier,
                max_components=barter_max_components,
                rng=barter_rng,
                pack_count=1,
            )
            if generated_scheme:
                normalized_scheme = generated_scheme
                warnings.append(f"Generated barter scheme for {item_name} ({offer_id}) from Price {clean_number(price)} {currency}")

        row = {
            "OfferId": offer_id,
            "TplId": sold_tpl,
            "ItemName": item_name,
            "Price": clean_number(price),
            "Currency": currency,
            # Keep cash-only rows cash by default. The runtime randomizer can still use the
            # generated BarterScheme when it picks the offer as part of the barter 15%.
            "CashOnly": force_cash_only_rows or original_cash_only,
            "BarterScheme": normalized_scheme,
        }

        if is_ammo:
            row["AmmoBarterPackSize"] = max(1, int(pack_count or ammo_barter_pack_size or 30))
            row["BarterSchemeValueBasis"] = barter_scheme_value_basis

            if ammo_pack_info:
                row.update(ammo_pack_info)
                warnings.append(
                    f"Ammo barter pack target for {item_name} ({offer_id}): "
                    f"{ammo_pack_info['AmmoBarterPackItemName']} / {ammo_pack_info['AmmoBarterPackSize']} rounds"
                )
            else:
                warnings.append(
                    f"No ammo pack template found for {item_name} ({offer_id}); "
                    "runtime randomizer should keep this ammo offer cash-only unless you add AmmoBarterPackTplId manually"
                )

        output.append(row)

    output.sort(key=lambda row: (str(row["ItemName"]).lower(), str(row["OfferId"])))

    root_tpl_counts = Counter(get_item_tpl(root) for root in sellable_roots)
    duplicate_tpls = {tpl: count for tpl, count in root_tpl_counts.items() if count > 1}
    if duplicate_tpls:
        warnings.append(f"Duplicate TplIds preserved by OfferId: {duplicate_tpls}")

    unknown_names = [row["TplId"] for row in output if str(row["ItemName"]).startswith("UNKNOWN_ITEM_")]
    if unknown_names:
        warnings.append(f"Unknown item names: {sorted(set(unknown_names))}")

    return output, warnings


def build_report(out_path: Path, output: list[dict[str, Any]], warnings: list[str]) -> str:
    cash_default_rows = sum(1 for row in output if row.get("CashOnly") is True)
    real_barter_scheme_rows = sum(1 for row in output if is_real_barter_scheme(row.get("BarterScheme", [])))
    cash_only_scheme_rows = sum(1 for row in output if is_cash_only_scheme(row.get("BarterScheme", [])))
    generated_rows = sum(
        1 for warning in warnings
        if warning.startswith("Generated barter scheme for ")
        or warning.startswith("Generated ammo pack item-barter scheme for ")
    )
    generated_ammo_pack_rows = sum(1 for warning in warnings if warning.startswith("Generated ammo pack item-barter scheme for "))
    ammo_pack_target_rows = sum(1 for row in output if row.get("AmmoBarterPackTplId"))
    ammo_rows_without_pack_target = sum(
        1 for row in output
        if row.get("AmmoBarterPackSize") and not row.get("AmmoBarterPackTplId")
    )

    duplicate_tpl_counts = Counter(str(row.get("TplId", "")) for row in output)
    duplicate_tpls = {tpl: count for tpl, count in duplicate_tpl_counts.items() if count > 1}

    lines = [
        "items.json generation report",
        "============================",
        f"Output: {out_path}",
        f"Rows written: {len(output)}",
        f"Cash default rows (CashOnly=true): {cash_default_rows}",
        f"Rows with real barter scheme: {real_barter_scheme_rows}",
        f"Rows with cash-only scheme: {cash_only_scheme_rows}",
        f"Generated barter schemes: {generated_rows}",
        f"Generated ammo pack item-barter schemes: {generated_ammo_pack_rows}",
        f"Ammo rows with pack template target: {ammo_pack_target_rows}",
        f"Ammo rows missing pack template target: {ammo_rows_without_pack_target}",
        f"Unique TplIds: {len(duplicate_tpl_counts)}",
        f"Duplicate TplIds preserved: {duplicate_tpls}",
        "",
        "Warnings:",
    ]

    if warnings:
        generated_warning_count = sum(
            1 for warning in warnings
            if warning.startswith("Generated barter scheme for ")
            or warning.startswith("Generated ammo pack item-barter scheme for ")
        )
        non_generated_warnings = [
            warning for warning in warnings
            if not warning.startswith("Generated barter scheme for ")
            and not warning.startswith("Generated ammo pack item-barter scheme for ")
        ]
        if generated_warning_count:
            lines.append(f"  - Generated barter scheme details suppressed in report summary: {generated_warning_count} rows")
        lines.extend(f"  - {warning}" for warning in non_generated_warnings)
    else:
        lines.append("  none")

    return "\n".join(lines) + "\n"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=f"Generate Tony config/items.json from data/assort.json v{SCRIPT_VERSION}")
    parser.add_argument("--version", action="version", version=f"generate_items_from_assort.py {SCRIPT_VERSION}")
    parser.add_argument("--assort", default="data/assort.json", help="Path to trader assort.json")
    parser.add_argument("--out", default="config/items.json", help="Path to write generated items.json")
    parser.add_argument("--catalog", default=None, help="Optional existing items.json/list used for readable names and fallback prices")
    parser.add_argument("--locale", default=None, help="Optional SPT English locale JSON used for readable item names")
    parser.add_argument("--report", default=None, help="Optional report output path. Defaults to <out>.report.txt")
    parser.add_argument("--cash-only", action="store_true", help="Set CashOnly=true on every generated row")
    parser.add_argument("--default-price", type=float, default=0.0, help="Fallback RUB price for barter-only rows with no catalog/tarkov.dev price")
    parser.add_argument(
        "--generate-barter-schemes",
        choices=["none", "cash-only", "missing", "all"],
        default="none",
        help=(
            "Generate non-currency BarterScheme recipes from Price. "
            "cash-only = replace simple cash schemes only; missing = replace rows without a real barter; all = replace every row."
        ),
    )
    parser.add_argument("--barter-value-multiplier", type=float, default=1.0, help="Generated barter target value multiplier based on Price")
    parser.add_argument("--barter-max-components", type=int, default=4, help="Max different item types in a generated barter recipe")
    parser.add_argument("--barter-seed", type=int, default=1337, help="Seed for deterministic generated barter recipes")
    parser.add_argument("--ammo-barter-pack-size", type=int, default=30, help="Ammo generated barters are valued as this many rounds instead of one round")

    tarkov_dev_group = parser.add_mutually_exclusive_group()
    tarkov_dev_group.add_argument("--tarkov-dev", dest="tarkov_dev", action="store_true", help="Use tarkov.dev API for item names/prices; default")
    tarkov_dev_group.add_argument("--no-tarkov-dev", dest="tarkov_dev", action="store_false", help="Do not call tarkov.dev; use only cache/catalog/locale/built-ins")
    parser.set_defaults(tarkov_dev=True)

    parser.add_argument("--tarkov-dev-cache", default=None, help="Path to tarkov.dev item cache. Defaults to <out dir>/tarkovdev_items_cache.json")
    parser.add_argument("--refresh-tarkov-dev-cache", "--refresh-cache", dest="refresh_tarkov_dev_cache", action="store_true", help="Ignore existing tarkov.dev cache and fetch fresh data")
    parser.add_argument("--tarkov-dev-timeout", type=float, default=20.0, help="tarkov.dev request timeout in seconds")

    return parser.parse_args()


def main() -> int:
    args = parse_args()

    assort_path = Path(args.assort)
    out_path = Path(args.out)
    catalog_path = Path(args.catalog) if args.catalog else None
    locale_path = Path(args.locale) if args.locale else None
    report_path = Path(args.report) if args.report else out_path.with_suffix(out_path.suffix + ".report.txt")
    tarkov_dev_cache_path = Path(args.tarkov_dev_cache) if args.tarkov_dev_cache else out_path.parent / "tarkovdev_items_cache.json"

    if not assort_path.exists():
        raise FileNotFoundError(f"assort file not found: {assort_path}")

    assort = load_json(assort_path)
    if not isinstance(assort, dict):
        raise ValueError("assort file must be a JSON object")

    catalog_names, catalog_prices = load_name_and_price_catalog(catalog_path)
    locale_names = load_locale_names(locale_path)
    tarkov_dev_names, tarkov_dev_prices, tarkov_dev_warnings = get_tarkov_dev_names_and_prices(
        enabled=bool(args.tarkov_dev),
        cache_path=tarkov_dev_cache_path,
        refresh_cache=bool(args.refresh_tarkov_dev_cache),
        timeout_seconds=float(args.tarkov_dev_timeout),
    )

    output, warnings = generate_items(
        assort=assort,
        tarkov_dev_names=tarkov_dev_names,
        tarkov_dev_prices=tarkov_dev_prices,
        locale_names=locale_names,
        catalog_names=catalog_names,
        catalog_prices=catalog_prices,
        force_cash_only_rows=args.cash_only,
        default_price=args.default_price,
        generate_barter_schemes=args.generate_barter_schemes,
        barter_value_multiplier=args.barter_value_multiplier,
        barter_max_components=args.barter_max_components,
        barter_rng=random.Random(args.barter_seed),
        ammo_barter_pack_size=args.ammo_barter_pack_size,
    )
    warnings = tarkov_dev_warnings + warnings

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(output, indent=4, ensure_ascii=False) + "\n", encoding="utf-8")

    report = build_report(out_path, output, warnings)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(report, encoding="utf-8")

    print(report)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
