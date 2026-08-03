---
market: GLOBAL
topic: warranty-discounts
exposure: internal
effective_date: 2026-06-22
version: 1
retrieval: exclude
---

<!--
Not retrieved. This is agent workflow — how to create a discount in Shopify, which code to
issue for which warranty case — and nothing a draft should rest on. Being internal was not
enough to keep it out of replies: the model never quoted it, it converted it. Shown "repair at
our headquarters can take 2 months" it told a customer "about 8 weeks", against a published
policy of one week that was in the same prompt. It also carries ITALY10, 50% and 80%, which is
the material behind every commitment defect found on 2026-08-03.

Measured: with internal guidance retrieved, class E failed 2 runs in 15; without it, 0 in 12.

It stays here for people to read. Delete this marker to put it back in front of the model.
-->

# Warranty discounts

Warranty discount code format: ORDER NR - WARRANTY → #US#1111 - WARRANTY

Go to the Shopify needed locale > Discounts > Create discount > Amount off order:
* the discount code will be valid only for that locale

















Make sure to correct edit and fill these options below:




















We create these types of discounts, when the clients choose this option instead of the repair or
replacement.

       ** IF THE CLIENT WANTS TO USE ADDITIONAL 10% OFF DISCOUNT,
           HE WILL BE ABLE TO USE ONLY ITALY10 DISCOUNT CODE **


                                      EUROPEAN UNION


        How much the client spend on the item in question for the repair:
             -   Up to 150 eur for the item:
                               -   UNDER 1 YEAR: Replacement - no need for return (check the stock first)
                               -   Repair at our headquarters (generate the FedEx/easyship return label)
                               -   Warranty discount - for the same amount spent on the item
             -   Over 150 eur for the item:
                      -   Order placed less than 3 months ago:
                               -   Repair at our headquarters (generate the FedEx/easyship return label)
                               -   Replacement - check the stock, the new item is sent once the return item is in
                                   transit. For the return - generate the FedEx/easyship return label
                      -   Order placed over 3 months ago:
                               -   Repair at our headquarters (generate the FedEx/easyship return label)
                               -   Warranty discount - 50% amount of the item

                                                         UK


        How much the client spend on the item in question for the repair:
             -   Up to 130 GDB for the item:
                      -   Order placed less than 1 year ago:
                               -   Warranty discount - for the same amount spent on the item.
                               -   Replacement - no need for return (check the stock before suggesting it)
                      -   Order placed over 1 year ago:
                               -   Warranty discount: 80% / 50% (over 2 years) amount spent on the item.
                               -   Repair at our headquarters (it can take 2 months, generate the easyship
                                   return label)
             -   Over 130 GDB for the item:
                      -   Order placed less than 6 months ago:
                               -   Replacement - check the stock/other colors, the new item is sent once the
                                   return item is in transit. For the return - generate the easyship return label
                               -   If we don’t have the item in stock - warranty discount for the same amount
                      -   Order placed over 6 months ago:
                               -   Repair at our headquarters  (it can take 2 months, generate the easyship
                                   return label)
                               -   Warranty discount - 80% / 50% (more than 1 years) amount of the item.


For both UK and EU orders suggest the repair option if the purchase is not
recent. In such cases, it is preferable to repair the item rather than send a
replacement, as this is the most appropriate and efficient solution.
                                                         USA


         How much the client spend on the item in question for the repair:
             -    Up to 150 USD for the item:
                       -   Order placed less than 1 year ago:
                                -    Warranty discount - for the same amount spent on the item.
                                -    Replacement - no need for return (check the stock before suggesting it)
                       -   Order placed 1 year ago:
                                -    Warranty discount - 80% / 50% (over 2 years) amount spent on the item.
                                -    Repair at our headquarters (it can take 6 months, generate the easyship
                                     return label)
             -    Over 150 USD for the item:
                       -   Order placed less than 6 months ago:
                                -    Replacement - check the stock/other colors and the new item is sent once
                                     the return item is in transit. For the return - generate via LOOP return label
                                     (check if its worth returning)
                                -    Warranty discount for the same amount, if we don’t have the item in stock
                       -   Order placed over 6 months ago:
                                -    Warranty discount - 80% / 50% (more than 1 year) amount of the item.
                                -    Repair at our headquarters (it can take 6 months, generate the easyship
                                     return label)

                                                    CANADA


         How much the client spend on the item in question for the repair:
             -    Up to 250 CAD for the item:
                       -   Order placed less than 1 year ago:
                                -    Warranty discount - for the same amount spent on the item.
                                -    Replacement - no need for return (check the stock before suggesting it)
                       -   Order placed 1 year ago:
                                -    Warranty discount - 80% / 50% (over 2 years) amount spent on the item.
                                -    Repair at our headquarters (it can take around 8 months)
             -    Over 250 CAD for the item:
                       -   Order placed less than 6 months ago:
                                -    Replacement - check the stock/other colors and the new item is sent once
                                     the return item is in transit. For the return - generate via LOOP return label
                                     (check if its worth returning)
                                -    Warranty discount for the same amount, if we don’t have the item in stock
                       -   Order placed over 6 months ago:
                                -    Warranty discount - 80% / 50% (over 2 years) amount of the item
                                -    Repair at our headquarters (it can take around 8 months)

                                                AUSTRALIA


         How much the client spend on the item in question for the repair:
             -    Up to 270 AUD for the item:
                       -   Order placed less than 1 year ago:
                                -    Warranty discount - for the same amount spent on the item.
                                -    Replacement - no need for return (check the stock before suggesting it)
                       -   Order placed over 1 year ago:
                                -    Warranty discount - 80% / 50% (over 2 years) amount spent on the item.
                                -    Repair at our headquarters (it can take around 8 months)
             -    Over 270 AUD for the item:
                       -   Order placed less than 6 months ago:
                                -    Replacement - check the stock/other colors and the new item is sent once
                                     the return item is in transit.For the return - generate the FedEx/easyship
                                     return label (check if its worth returning)
                                -    Warranty discount for the same amount, if we don’t have the item in stock
                       -   Order placed over 6 months ago:
                                -    Warranty discount - 80%  / 50% (over 1 year) amount of the item.
                                -    Repair at our headquarters (mention that it can take around 8 months)

For US, CA, and AU orders, repairs can take longer due to international
shipping times. Because of this, the priority should be to offer alternative
solutions first when possible.
* If a client asks why the repair process takes so long, politely explain that the extended timeframe is mainly
due to shipping logistics. To ensure the repair is carried out correctly and meets our quality standards, all
repairs are performed at our headquarters. Additionally, we do not ship individual items separately for repair,
which can add to the overall timeline.

                                                    GLOBAL


         You should evaluate both how long the client has owned the item and the extent of
         the damage before proposing a solution.
             -    Start by offering a Warranty discount valued at up to 80% of the item’s price, depending on
                  the situation.
             -    If the customer is not satisfied with this option, you may then offer a partial refund, also up to
                  80% of the item’s value, based on the specific circumstances.
             -    As the return shipping cost in most cases are too high, we don’t ask the clients to send it back
                  to us.

         Important: Avoid offering the highest possible amount right away — start lower and
         adjust based on the discussion and the client’s response.
