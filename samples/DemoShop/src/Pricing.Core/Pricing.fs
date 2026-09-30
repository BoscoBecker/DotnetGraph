module Pricing.Core

type Money = { Amount: decimal; Currency: string }

let discount (rate: decimal) (value: Money) =
    { value with Amount = value.Amount * (1m - rate) }
