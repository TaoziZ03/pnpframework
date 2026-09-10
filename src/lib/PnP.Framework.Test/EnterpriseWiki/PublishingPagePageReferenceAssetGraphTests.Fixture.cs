using System;

namespace PnP.Framework.Test.EnterpriseWiki
{
    public partial class PublishingPagePageReferenceAssetGraphTests
    {
        // Exact CCD-146 claimed PNG, not a live-readback or source-page fixture.
        // SHA-256 84b3dcb35fb0ff34d2a95b0f5cbf9cfd683bb05f6716d6819946000a1edf742f; 7,862 bytes.
        // Only image bytes are embedded; tenant sessions and unrelated page data are absent.
        private static byte[] ClaimedPng() => Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAZQAAABgCAYAAAA6sYQbAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsQAAA7EAZUrDhsAAB5LSURB" +
            "VHhe7Z1/bFTXlce/U2k3f2wgSCkefm3bbbB3gmMghFQdS+vFFYTageBVSsiqZGlXYSygTWKLf3BgMYWQf9hxgARkE23LJitBAIkGMxNKhQPS2lIaEsA1nozZ" +
            "qttNMDZFImFVKV0pb8+57743d368N2N7sI19Pugx9/eP9zz33HPvm3sCFgFBEARBGCFf05+CIAiCMCJEoAiCIAhFQQSKIAiCUBREoAiCIAhFQQSKIAiCUBRE" +
            "oAiCIAhFQQSKIAiCUBREoAiCIAhFYRIIlJtoqWxGILCDroNo6dPBBn0tBynuBOLaP3ISqPeoiyl+fYIgCGPP2AuUvhZUBio9B9+RMx0Nnc2wktUI65BMShs2" +
            "wLKeRo32321Guz5BEITRYEwFSrw+gEDZMaxOdqKhlENMbYIvPYvvu0BCh8PJ77p3oJ4j/eLykk97YU3DiTfTeLSTUW04gjYMorFMx1degF10nvqM9mfXRf64" +
            "E5+rrYIgCGMMn+U16iSjFmkLFiIxHZCbZPQApenVnvOU54AVjpy3kq5/u6WK8Itz0GmiKkEGOeN6rQiVEY4OGv7c+dPaqfBOq8hVX2abY8ctuGns8tz+cVxY" +
            "uwVBEMYJo66hpLQSC1Zr9qKPvb9gz+zLGgd1aIrVm6uglJnSKmyLAG0nEyqc8YsbMvEetIW/h8MN03VACK3WBq1J5W/nUOlr70FX5Fm4t6SmCtHwAI6139QB" +
            "A6io0/2bOx3hrpu4psIFQRDGB6MuUGpaSZAkV+NYWX32pnT8BA3OJYhZ20lz2o5ktERHOJQgpAd0Zm4oqF2MX1yRydvOYjAdoQrtVAQRmqudgiAI45Cx2UMp" +
            "bUBnDKgNBFBpbAb0JWimH54Oe9xMYE/jgHKl6EZtvaN12PGRupD2+8UNg5pyRLrOYY8h9eL1zdTemwW080GESLtIaBXCyedH6YpyhNuOpPZ+SGjVtgWxeoWj" +
            "IQmCIIxvxtweSktlAI1dYUTVxjxvQL9B/gDFPIJodBCNvJwUWQNr8yAqy25iGwui2t+qvJHYdnuJiDezveJoYA7oMBMVD584t9xz6AK3hwhXI9nJy04+7WzV" +
            "Qsys18nn15as+kronvASW3pdMasKCcfvtkcQBGHsuXcMbDlCI9frtn5xgiAIwqgw9r9DKQR35v5b1AZ2pC8f+cUJgiAIo4aYABYEQRCKwr2hoQiCIAjjnjET" +
            "KP39/dolCIIgTAREQxEEQRCKgggUQRAEoSiIQBEEQRCKgggUQRAEoSiIQCmIq3hzShMiU/bhdBFOZOzfv4/KOopL2p9N8eprqQQCAfvKPPLeL2441OuyzKuy" +
            "RUcOk3h9ccoZEol6BJr5rLk46psDcE/0UXBYJUbt506+bRGE8YUIlIKYh+fvrMV3tG+kzPzpC2i7swYLtT+b4tXX0AlYSeQ0LuYXNxxaLSBCn1Eqk3/dxFdF" +
            "48iEVU0rEONCR5tgCHPp3908Y7RgxlNbBMGHSSBQBnG6ugn1D7ysrpRmYIcrLeBMB36WSyM4c9TN9+YZHcZcs9Pb4bY2kZbGiE8vM9WWnNqHV30OcXu2nmk8" +
            "zNQ0+Mo6xXk4kBCoNMp0hIKjhaQOsUylicR0mAPl6dZOJl87MzWcNEF0LBXuaCtOepXOaEfMaHvccBdmdI14MIRwSUifkRZG6EHlAG62oLK5Fm2BLjQeCCCw" +
            "g66DLbbxNBVnh9UnbG2C3ZFe7Xa0GtY4OJ/SOjROmL7StBCvtgjCeIR/KT8WXL9+XbtGl+v79lrrN/VoX4916P691o5N5yzVmveOWOuXGG6Ka+9jj2V9vGlL" +
            "mt+yBqz2JVusyNQmuzxOz59956wd92+xDr2nk2WUo1BpMsLy1kfE7Hm/n12yZJTSRLTHIWlZYcqX0+BXrjgdljL2Zder0rCby9dpOEmM/GGql4lQWEo/oSus" +
            "kmaR2U7O55TBsN9pE5fvVZ6ZzmyT4w5TXhWt/etPU3oqI62NzuW0ZzBqhY/bnY8dj9jlucSsyPaw5dpdSyNpRQ/AQjNdnL83Yn9m5uHyt+tyOY0Zl+n3bYsg" +
            "jC8mxZIX71k4M//mrZlHzd/ArJXVmMnOh0ow58NBcIpLpy4D65biSW2DZOFLyzDHdqZhPfdDtL0+D1i+Rn32x7vx6bq1eH65TrC8GqsW38DFuL8RroLqq7GH" +
            "PnUysUEfzdid2XlZow4cAX3tQFfEqIc+o2FSEiic3RFSO/pIu+oib4Km5wnyVxiWAswlrwglWqc1Cs920lS9jco/3KD9BC+fOcbMmHCUyqU2JTsxpNOVV2/W" +
            "6ek/Nrp26Jd6qU+3L+1qVVmA6Q3ofNrufM3TrUM/cPSRGCzOH2q1P32I97TRDTE0nqO2/1iv0ntG3hZBGEUmvkA5cxTbm4LY8PkraKWreVfmQvQMzHxIOzOY" +
            "87df1y5ibhCztNPkOytJmPhSgtnl2pmHQurLggZjHpxjelBM0sB7NzCNfdWRuz1BA3yMhAkJmQQJjTqPkW4ztaeLl3BG2M4uyttIY61pn6YQ0o2u0X9Ud+bS" +
            "m3vV2+lGSqR8iMN+SRTJ7RYs4+qsGorYFITxwYQXKP1J0jcWl8AWI1dxuumGcuVj4coF+LSpw30Tq3//r/GBdvsxs6YCcw6/ndoDIYF24PAMPFbjb9WxoPpo" +
            "MOWBz9wL6OPBmmb3jjHHPUXQUEpXUJE0eJv7JLXkX03hTE0dDe5UT4jGzdAx0i5o5u81hF6j9oVpIPdtJ2VmTSbdmFn6m12soVgkwNpqvfdC4ntsrcmk1hAS" +
            "XGfkHwrQUHzhzfEuJG7ZvviJACovaG2iQOIXGt121pTTzRtsxB6+Pwp5m0u4h9FLX6PO6O2hGHsd9x+x2vftVe7nN/5nWvjHZjq9j8L7Lezna8e+c9ah+1P7" +
            "Gmacu+/ioPdRVJy5D/LeETePeTn7LX71KWL20Je5h2LuCUR5b4LdvB+g02deKr9fHKP3HJzwrD0WvZfBeyFOnqz9E76MPQ/PdjIZ9Tn5nP0T3l8x8zvNNPsR" +
            "oTSqDEr3iS4vZsa7mUYI73PwPglfB6Ju/5LnwznDFUaeyHneQzHSmOXlyisI9whjdnw9Hw45c6bauRCE4sNvd5UB2+ive4gLUIIgDBP5HYow8dDChJeVagPp" +
            "S2eCINw9REMRBEEQioJoKIIgCEJREIEiCIIgFAURKIIgCEJREIEiCIIgFAURKIIgCEJREIEiCIIgFAURKAUx/g1sOQcvepxKMnrkNU6lD0F0jn3PIuO499HE" +
            "OYLePFo+jT60HNTtH0n7VD3pdfRdqPSpd6gUqZ0j5i4+SzE8Ni4RgVIQ49/AVmmDfcDIuPhVuIdBqPiJWuBZ+/DDZPkx4/wqkxq0NseUoa5Rh0/23RT1MThW" +
            "ioYN1P6Nfmnyo87yqqhLe1alVZ2wmot1mnBx2jly7vKzFMNj445JIFAml4GtXNYRPQ1X8S/Kc4Q75bVQPU58rnJz4mkQKo6Tg1FsDrFgCaCsowvdg0ahhpGp" +
            "tNmmh+EqO40xE+fLneFnpiOM8iO9XvkMvNrjh5HHMx/1Z2d3GNEqR3Tk0yZSffFOMxTy3DOnfKcvFB9z8nBcwtHiMtuRp529HvfT6575PndCDI+NT/iX8mOB" +
            "GNjKCMtbH6EPOsx5yKE+DDHtEEfC03CVTu+Wpct28qt8jnEqjuMDF8k5fONUMSvChx72RqzweSrV+WT4cETDqFTsONKNTFErchuuSkcdzuiEO2kMY1Zcrlun" +
            "QVo+lV7XY/gjvbZXocLM9hEZfcjya1TfcrQ9Z5l8z6juVJv9jHvlIGeZ6aT1PbN8ld95hnZc+LhxoKV7iKVfO+04tw4zX9575vPcuW3aLYbHxg+TYslrohvY" +
            "8oSmnl6Gq3yNaGkq6njxhKA2hbuA/yLnSI1TXRs0jQLbKCNTFdvQMN3211R5LNXkMFyl9h707Ja1HpdQHSKDCfTdSoB0ISRu9iFBj6CixJ7TeuZTGBoE9efw" +
            "98Jo68nSYdLIayiLydJO8pA4ibZgFIdd2yi8hNTp3qfh4t93P7pQUd5g/02whjCQgFKy87bT6LORr6B7xuQyWCaGx8YlE1+gTHQDW0XENKLFhBzjJRqSGyM2" +
            "TjW36jBW95SpwaPrZmrNLzzdqGx6CBlNUWQZrkrUo+xcBWLaKFWy2hRDNagrSaCdhEj02dVI9LYjMRBBHRvZ8s3HVCA0nEE7j6Ese+8kJTjHhLx99+MuLC0V" +
            "YFxsyAbLhDFjwguUiW5gyxf6HnoZrspnRCsXVPUIjVMxesOYBw9nhlkeQde5PdwERd+FnaCm5KWPtR21McvEsedc+my7hgR54zkSjKEVCPU0ok1vhOfLR3od" +
            "drpGs+z4fINaXkNZQ9VOGNayBswyqdRhGPQyyd/3FKYhMF+G2U4xLjbxmASnDfNG+Gt496MArK/mY9XuAby7dQBfrX0SdVfb3fCNd6rxmZNu0VLs6KgG9u9z" +
            "l8hm71qKWU1naZCfgVUfv4BF8VSck97tDW/KP3oWn32Nyw6q9Gopizfdn7lipzF4/J1X1BIZL8151afy04gbqCUhETOWqnRYJm4a+k47R7kraELq2mXPiGNb" +
            "8LwcxlpIow5kk70Jx2/mHQY8yJwst9DKFhwvVGJPSadyM+x3ll/C1VFUnCMBwEslmzqxojcVp2a0G/Syi9osLkMjryZaEUS/141GTucskfDG7nHgMKVHWn0e" +
            "+R6KIHyNNCcYYUS4OmnPmnkDmZdlMoissfuUFW+0lfteC90uk3xlch/eoIGdpTmT1n8PfMvMc8+MvBF6Dt30HLpKtmBL4FW8qvPEmjcj4ZThtCdHOz/ZsAKn" +
            "jbpizXU42VyLNk7j5PO5Z+bfREH9FsYcOb5eGEV4BpoxoNgRExzu906ESDiO6XKXINxlRKAIgiAIRUF+2CgIgiAUBdFQBOEeI21vIQfuno8gjDIkUHrHSKA8" +
            "QALlc+0TJjq3/s94JVqYNDz4F3/ULmEyIEtegiAIQlEQgSIIgiAUBREogiAIQlEQgSIIgiAUBdmUL4g/4M0p7+MDTMOqj1fiybnOz4GHR//+d7G9aRo23vk7" +
            "LFQHmmQylPr+Ei2V39a/bP8S0eTv0FCap319D6KybDq68AVi1meoydkGL4ZRH8Gb8tblo2hqA557vQJXfvI2EHkFzy4IwLrRgX07z2KAz5XRWFYQT2z7KYLx" +
            "l/H2xezyv1pE9+7iBVwx8gRXvIgXv59+xM3Vnzep/NaspXhiJtWxyK7TC8saxPuvvoaz1ymPNZ/a+gzmGXUUgmVdxVHq32XMUH2onjG0/JkMvrcPLaeCnm0Z" +
            "Sn3F6F8uvJ6tbMpPLkRDKYhv4Pk7S4poYOsptN2p8hAmzFDq+zMaOhOwkjf5ZJTCKL2FzqGkT2MY9ZnMLsHX6V9wtvYTgRnVePGN3Xhl61IESZAs27oLr77x" +
            "gjswWo/9MC1u7WMWAoFvYM22ZRQ2H2v371LxOPUatvz8qp2HBs6O3U14q38ZXqL43f8MXPlQRfkSCJSguonasn4+ua/gSvZJOXkJBOZhzetrsUD7R0rJ91+g" +
            "+7HGc+AfSn3F6J8nOZ6tMLmYBALlNk5X/zvqH3hLXZEpF3BJnZtrh0emvIvTZ67gZ1O0+5qhsJ254OZLN7Blp7fDWZtw3DqvEZ9eZqotWXUxXvU5xGfRIBBC" +
            "fTwjnyesTYRUHvuahbjqu0F8thufVq6uK2fccAmWYMbMIOzTR2YgWERLeyyUXiABM+PDt3HksoXeX7yGX2EZGrYsQQkNxBy/bLFOnAc1iz89gGXrl2Lg9PsY" +
            "tCxXQG3ZtA8dlzuwd5N230jdFzVL/8nL6jpqDNSsgXF6Dj9y+SqOuG47rxlvlunUmRnu4FVfPnL2T7dhy6ajuJrWXv84l7v4bIV7h0kgUKbhyY5/Quvnz6lr" +
            "x+7bOPCT/9HhthZw8RSw/s5zaDs+Db9c341+HnRpcI/84DaeurhW5Vt8ipegNHPn41/uPIWnFln4zTPn8cG6JWh955v44BSVy8Lk0UuYddyuT5X56CktPHRb" +
            "Li7ItnXiV9+w0dqEZV/J6H2orZ+q45ipqD15x44njaO79mFbcLAwqb0P0WSvHRf7Am2130ZLX/qAlk6m8DKu+ik6jQ3PkoM0+BQy6Mz78W68+uN0EwG5whTB" +
            "csw3Z8dUBwsTB87nt9zlMtCDK6hA+XwqD93oGbDbvGSLrQVc+Qj4x9dfwe5IEL/6Nz0gqyUfGqRJg9pNcfM/4iUoGyXsXn8Jy2ZZuHLoP3B58VqlIVz+iO4v" +
            "DdC83BeMUHlOmTv3K+HhahOkfc3QZTn41ZeXXP1zBDIGcDZu94/rHWgjYTWwxDMuTagQQ3m2wsRjUix58Z6FM/Nv3pq5b3Mbs1ZWYCYvPz00DXM+vK0NbP0e" +
            "WLfQ3b9Y+NJCDwNbf4+2178BLK9Sn/3x3+NTEjDPL9cD1/L5WLX4Ni7G/feLCqqv5roa4FtrChgUNX0tf2MP6nSVNd6nQx2+RHTzF7az9BYOR79E28mpiNMF" +
            "3IfGsoftvLW2/1h7Zn6TdOGVdrXeUSnU0pYWBPN+7L2EM9b0xs8Ci8pJGJWgfBEJkEumLdsbCC6ytR41K/9sELxL0PsRDeeLl7rLdA/X8ACcDS/fsTAMLFij" +
            "Pm9e6sYNEjCuoJtfjSdm38ioM5tC68uFf/+oCTXpWh0LPgevuHvl2Qp3l4kvUGjmzxvgGz63Z/7Nux7QEQ7TfAxsGbP5udM8DGz9tXZ5MW0IBrby1zckSNMo" +
            "a/wSMcvWNJIkMNL5EiGvEzrCN5HU+Zyrs+HPOjIXhWsodw2eeX+m3Uz/gNIehgJvcPNey0D7XrWs81r7AG6c6sBVtxzv5ZwZM43TAChRrmQLFj2sXbmxZ/ja" +
            "k4dC6sskf//StYuvzzTFlF+cIEwCgdKfvE0zuWn6y/YHnG4ifwEsXPktfNp0Re+3sJZzqaAlqJk138Kcw++n9lNIoB04PA2P1WQKsnQKqm+Ieyh9CdIowl9q" +
            "Y0pTsCdLQ5mKnS1OmB0fqfsCNXSha7phmGsK6vPWm19DuZs4S0fObP/hH72EJ3AWLa/aS1IMv/G19z02zuHNzTO/xuXZeiOfl3b2v0gaw+W8m9cPL1qQNjCr" +
            "cpTLn+kLK9x9H4aXst76cAbmL/Q/53649eXv32W89Qtb62Dhc+7UDUMI+sUJAk2IJv5rw7wR/q42pPVNrNp9G+9u/RxfrV2Muqu/ccM33pmPz5x09GXd0VEB" +
            "7D/lLpHN3rUAs5p4kLdf5V0UT8U56dWyGaP3UWwDWw+kXv3lTfdn/ttOY/D4O2yDPqCW5rzqU/nV3sZURGK9qWUvHZaJneY+4xXfLxCN3odGFirPfIHwO1PV" +
            "a8NuGBGO/i6lhWSWyxpL5x9Rypv4nvV5L3P4neXlCINcrw07Szru6796ych5VbbQ14YZfnXY2aTPBb+eyzN2M53awH71NfyKNJ+/IkXrT//LYfy6bTUGnNdv" +
            "dXqc2a/yM8EVSxE8dZaGYPtV3vJLqbjMdpj9N/vNwuXlQ9mSbP76XUpoOu1lMutz7puJX/+4HyVV30Xg/J+wLAK8ret16rLbOJgzzg95bXhyIb9DEUYFORxy" +
            "/OMKjRy/TfGL80MEyuRCBIowKohAGd9kaommtucXlw8RKJMLESjCqCACZXIiAmVyMSleGxYEQRDuPqShuO8LjipisVEQBGFiIRqKIAiCUBREoAiCIAhFQQSK" +
            "IAiCUBREoAiCIAhFQQRKQVzFm1OaEJmyD6ev6aAR0L9/H5V1FJe0P5uh11cfAFr6tEfDYfzTgczwMSNRj0BzPeL0r745gPqEDlfYYYEddB1sQeFN5nyVaPE/" +
            "SzGDPrQc1HUNOa/BzRZUqv4wZplO2MiIn9DlZV7Hd9r3aiRtHw6+z08QRKAUyDw8f2dtEQ1svYC2O2uwUPuzKU59rRYQ0e5xQzCEufQvlHGSYfxELfCsBWu7" +
            "hWT5Mey5q4NVKRo2UF0bo8M0MmYTv9CIroo61CifLnMN3fFAG04Wq/2PxOx2WmFEN1qIPUJhgcVobY6NzbP1eH6e9JHQDZDgGy+TGuGuMgkEyiBOVzeh/oGX" +
            "1ZXSDOxwpQWc6cDPcmkEZ466+dINbNnp7XBbm0hLY8Snl5lqS07tw6s+B5r2ssZRb05/6YtaSWFt5Gwss+Pp+5s+w2/X4Zl5dXlpcbq8QL2t4XBYC5XH8W7W" +
            "XPkK4cEQwiUhGnqZMEIPKgcRx8nBKDaHyEWz8rKOLnQPGj3gmbE5Q3c0GKUh1KIt0IXGA6m4pAon9wm7Q0PWHDLqyzkTpzp2dtMgX2WLExuqq6Mb0TVRdHeY" +
            "bdR1O26nTL+4QulNtTUtX64+OHWcqNcahr435n3x67vn8/OhtAGdydU4VkblFfyHItyz8O9QxoLr169r1+hyfd9ea/2mHu3rsQ7dv9fasemcpVrz3hFr/RLD" +
            "TXHtfeyxrI83bUnzW9aA1b5kixWZ2mSXx+n5s++cteP+Ldah93SyjHIUKk1GWN76iBj/ZsiyIvSZSYTCo0ntMeBwRLSH84ctSyXTZbl5DH8yartj5A7TJ9cX" +
            "ozLCFO6V718/oTAqm91Zl1P/YNQKH6cMROx4hLNqYlbkQNRK9kas8Hkq2Pl04raHreig9lIcOK32ZsU7UF3R3pQ77JEmK5zLz6wvR97YcVjQfXHh8lTbklb0" +
            "gJFH1xM+rtut/LAi3D6/OAedJr0N3G+jDeZ98elD8nyY3HTvjXq4L8599+0758n5/AojRn+MJIhy/p0KE4NJseTFexbOzL95q306a4obmLWyGuonlg+VYM6H" +
            "g9rA1mVg3VI8aZ/9joUvLfMwsPVDtL0+D1i+Rn32x7vx6To+PVgnWF6NVYtv4GLc/9j0guqjyTAP0a3mpLgAopu1g8oOd5ECRc74STvI1Wpqbf8x0mYUEVWd" +
            "IqTbxHjlO34aaOhMEyOpq9VOg+k0W33aLrXm6Va3fIdrg93aZcLLK4YGcgSIbWjQs2QfqK5Qj73H0Nd7DF3B1VjhfyK8It5Dup6p8Ry1/cd6DY2JZvrZ2gnl" +
            "vdAIlK+gtpViRTndSzMPsbpKt5vatq2CtMqe1IzdL84bow2sPQwk7Gebrw/uMl26lpE3X57nl4+aVguWaCsTmokvUM4cxfamIDZ8/gpa6Wrelbn4O8PHwJZx" +
            "/tTcoIeBrRymaNMoGYKBrfz1FZUwkMwY/DsbdJwfHvmcpbGsq17ny8PcqsNY3VOmBrKum856oN6b2B4jRYe8gTbUFrh0VVNFg9cblVjX04WwGugLpCSK5HZ7" +
            "P8e5OqtSue29k21oSBNQcZwkedjVQe2nwZiX7brO7THaWYGQkX7udHP3xi9umOTpgyfDzVcQcdSXHcNq+uOxhjorEu4JJrxA6U+SvrG4RBvYuorTTTeUKx8L" +
            "Vy7Ap00d7ptY/ft/XaCBrQrMOfx2ag+EBNqBwzPwWI3/6awF1UejEw/QuSZ3IRqDEnoMjtMAXtliu72oqaP/SFtJGdFK7Zn44Zcvr4aSF0d40KVnwmpNX+2Z" +
            "1KC1mcLVRno3Eu7bTbYGk7hl+3gPpvJCaka9rYIG9oEwVj9c2KBYU06q2WCj8VJAxhtNHtpJ34WdaAuag3ES0aC5OU+CUO3pMHHsOdeFSLlThl/c0MnbBw+G" +
            "m68Q+mi2EWB1NtaJhmLJJ2HcMQnO8uKN8Ne0Ia35WLV7AO9uHcBXa59E3dV2N3zjnWp85qRbtBQ7OqqB/fvcJbLZu5ZiVtNZGuRnYNXHL2BRPBXnpHd7w5vy" +
            "j57VBraCKr1ayuJN92eyDSY9/s4raomMl+a86lP5aczh72QklmPZS8cpSLh8QoP7adIYbONa9D2mp3ySBv429rCGQfGlZh6G8x0GflSmZAbC/BoRZeii8GgF" +
            "lUXuaJIEBwmuzHyqPO0dDiwITpZbaA3x4FyJPSWdyq0ECi+9GETW2OlczDQ8wzaWxLissp7VaWG5ymTccjPjjTK5nbWIpQQeoeogjSS9bt7wLkPjIPDdx7cg" +
            "8MEtbHsWqNXlunXxRvkbidxxGlXnb8nBb3ypelNlw4og1lyHk+rlBPI7bcjRh09+QM/2DdKuKF34EXq43aQJBqOIljSikYXkJhrsb3n3fVjwW15lVGc4Cqsg" +
            "9Ve4l5HDIYVxAs+GMwZFO2IE2APvsfJkkZZtuI07EeKBt4D9GBdHaDTn2HfwixOEewwRKMIExBBODP+GY6hCoFgogWFrBUy42hBufnGCcA8iAkUQBEEoCvJL" +
            "eUEQBKEoiEARBEEQioIIFEEQBKEoiEARBEEQioIIFEEQBKEoiEARBEEQioIIlGGTQH3goKedh76WgwgETvAP2IeEdz7/+gRBEMaaySdQRsngT2nDBljW00P+" +
            "9fNw8wmCIIwtwP8DTy1FPhmktSIAAAAASUVORK5CYII=");
    }
}
