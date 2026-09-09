using God2.ReadableDatabaseBuilder;

namespace God2.GameplayContentRecovery.Tests;

public sealed class OfficialSkillBookCatalogTests
{
    [Fact]
    public async Task ParseAsync_ExtractsReadableSkillFieldsAndDropsMachineNumbers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"god2-skb-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            {
              "records": [
                {
                  "clientItemId": 6501,
                  "idSemantics": { "clientDisplayId": 20001 },
                  "itemType": "SKB",
                  "name": "剑一  苍穹诀。剑气",
                  "description": "剑客用特技书",
                  "priceCandidates": { "systemSell": 100, "systemRecycle": 10 },
                  "statCandidates": [
                    "技能 剑技 第1阶",
                    "使用消耗 5 MP",
                    "攻击距离 3",
                    "范围  一体",
                    "中威力技",
                    "8416",
                    "1"
                  ]
                },
                {
                  "clientItemId": 6502,
                  "idSemantics": { "clientDisplayId": 20001 },
                  "itemType": "SKB",
                  "name": "剑术心得",
                  "description": "被动技能书",
                  "priceCandidates": {},
                  "statCandidates": ["被动技能 剑技 第2阶", "9001"]
                },
                {
                  "clientItemId": 1,
                  "idSemantics": { "clientDisplayId": 1 },
                  "itemType": "ETC",
                  "name": "不是技能书",
                  "description": "忽略",
                  "priceCandidates": {},
                  "statCandidates": []
                }
              ]
            }
            """);

        try
        {
            var entries = await OfficialSkillBookCatalogParser.ParseAsync(path, new TraditionalChineseConverter());

            Assert.Equal(2, entries.Count);
            var skill = entries[0];
            Assert.Equal(6501, skill.ClientItemId);
            Assert.Equal(20001, skill.DisplayId);
            Assert.Equal("劍一  蒼穹訣。劍氣", skill.NameZhTw);
            Assert.Equal("技能", skill.ModeZhTw);
            Assert.Equal("劍技", skill.CategoryZhTw);
            Assert.Equal(1, skill.Level);
            Assert.Equal(5, skill.MpCost);
            Assert.Equal(3, skill.AttackRange);
            Assert.Equal("一體", skill.TargetScopeZhTw);
            Assert.Equal("中威力技", skill.EffectTextZhTw);
            Assert.DoesNotContain("8416", skill.EffectTextZhTw);

            var passive = entries[1];
            Assert.Equal("被動技能", passive.ModeZhTw);
            Assert.Equal("劍技", passive.CategoryZhTw);
            Assert.Equal(2, passive.Level);
            Assert.Null(passive.MpCost);
            Assert.Null(passive.EffectTextZhTw);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
