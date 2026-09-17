namespace AuraToolsExp.Dll.Features.CustomCards;

internal static class CustomCardLuaSupport
{
    // Shared by production generation and executable Lua behavior tests. No preview-only backend.
    internal const string Source = @"-- AuraTools.CustomCard compiler 1
local __self = self
local __f = {}
local function __number(v)
  local n = tonumber(v)
  assert(n and n == n and math.abs(n) <= 1000000, '自建卡牌：数值无效或超出范围')
  return n
end
local function __divide(a,b) assert(b ~= 0, '自建卡牌：除数为零'); return __number(a/b) end
local function __clamp(a,l,h) assert(l <= h, '自建卡牌：范围上下限颠倒'); return math.max(l, math.min(a,h)) end
local function __alive(o) return o ~= nil and tostring(o.state) ~= 'Dead' end
local function __one(o) if __alive(o) then return {o} end; return {} end
local function __copy(xs)
  local t = {}; for i=0,xs.Count-1 do t[#t+1] = xs:get_Item(i) end; return t
end
local function __restore(xs, status, target)
  __self.Object:Clear(); for _,o in ipairs(xs) do __self.Object:Add(o) end
  __self.status = status; __self.Target = target
end
local function __with(objects, action)
  local xs, status, target = __copy(__self.Object), __self.status, __self.Target
  __self.Object:Clear(); for _,o in ipairs(objects) do if __alive(o) then __self.Object:Add(o) end end
  __self.status = nil
  local ok, err = pcall(action)
  __restore(xs,status,target)
  if not ok then error(err) end
end
local function __objects(kind)
  local filters = {Enemies='AllTarget',Friends='AllFriends',All='All',RandomEnemy='AllRandomTarget1',RandomFriend='AllRandomFriends1'}
  local result
  __with({},function() result=__copy(__self:SetStatus(filters[kind])) end)
  return result
end
local function __context()
  return {vars={},target=__self.Target,budget={steps=0},battle=CS.FightManager.Instance}
end
local function __child(c)
  local v={}; for k,x in pairs(c.vars) do v[k]=x end
  return {vars=v,target=c.target,current=c.current,budget=c.budget,battle=c.battle}
end
local function __active(c)
  local f=CS.FightManager.Instance
  return f ~= nil and f == c.battle and tostring(f.fightType) ~= 'None' and tostring(f.fightType) ~= 'Win' and tostring(f.fightType) ~= 'Loss' and __alive(__self.Self)
end
local function __tick(c,id)
  c.node=id; c.budget.steps=c.budget.steps+1
  assert(c.budget.steps<=4096, '自建卡牌：执行步骤超过 4096；积木 '..id)
end
local function __read(o,field,buff)
  assert(__alive(o), '自建卡牌：读取的对象已不存在')
  if field=='Health' then return o.CurHp end
  if field=='MaxHealth' then return o.MaxHp end
  if field=='MissingHealth' then return math.max(0,o.MaxHp-o.CurHp) end
  if field=='HealthPercent' then if o.MaxHp<=0 then return 0 end; return 100*o.CurHp/o.MaxHp end
  if field=='Shield' then return o.Defend end
  if field=='BuffStacks' then local v=o:GetBuff(buff); if v==nil then return 0 end; return v.buffConfig.Level end
  local p=CS.FightPlayer.Instance
  assert(p~=nil and o==p.Status, '自建卡牌：这个数据只属于当前玩家')
  if field=='Energy' then return p.CurPowerCount end
  if field=='MaxEnergy' then return p.MaxPowerCount end
  if field=='HandCount' then return __self.HandCard.Count end
  if field=='DeckCount' then return __self.DeckCard.Count end
  if field=='DiscardCount' then return __self.UsedCard.Count end
  if field=='CardCost' then return __number(__self.dataConfig.data:get_Item('Expend')) end
  error('自建卡牌：未知战斗数据')
end
local function __sequence(c, fs, done)
  local i=0
  local function nextStep()
    if not __active(c) then return end
    i=i+1; if i>#fs then return done() end
    return fs[i](c,nextStep)
  end
  return nextStep()
end
local function __repeat(c,n,body,done)
  n=__number(n); assert(n==math.floor(n) and n>=0 and n<=64, '自建卡牌：重复次数需为 0～64 整数')
  local i=0
  local function nextStep()
    if not __active(c) then return end
    i=i+1; if i>n then return done() end
    return body(__child(c),nextStep)
  end
  return nextStep()
end
local function __each(c,objects,predicate,body,done)
  local i=0
  local function nextStep()
    if not __active(c) then return end
    i=i+1
    while i<=#objects do
      local child=__child(c); child.current=objects[i]
      if __alive(child.current) and predicate(child) then return body(child,nextStep) end
      i=i+1
    end
    return done()
  end
  return nextStep()
end
local function __waitHand(c,done)
  local iterator=CS.Witch.UI.UIManager.Instance:GetAllUI():GetEnumerator()
  local ui=nil
  while iterator:MoveNext() do
    local candidate=iterator.Current
    if candidate:GetType().FullName=='Witch.UI.Window.FightUI' then ui=candidate;break end
  end
  iterator:Dispose()
  assert(ui~=nil,'自建卡牌：战斗界面已关闭')
  if ui.createCardQueue.Count==0 then return done() end
  local finished=false
  __with(__one(__self.Self),function()
    __self:AddEvent('EndCreateCardItem',function()
      if finished or not __active(c) then return end
      if ui.createCardQueue.Count==0 then finished=true;return done() end
    end)
  end)
end
local function __effect(c,kind,objects,amount,buff,done)
  if #objects==0 then return done() end
  amount=__number(amount)
  assert(amount>=0, '自建卡牌：效果数量不能为负数；积木 '..c.node)
  amount=math.floor(amount)
  if kind=='Draw' or kind=='Discard' or kind=='Burn' or kind=='SelectFromDeck' or kind=='SelectFromDiscard' then
    assert(amount<=64,'自建卡牌：单次牌堆操作最多 64 张')
  end
  if kind=='SelectFromDeck' or kind=='SelectFromDiscard' then
    if amount==0 then return done() end
    local original=kind=='SelectFromDeck' and __self.DeckCard or __self.UsedCard
    if original.Count==0 then return done() end
    -- The native overload requires List<IDataConfig>, not List<DataConfig>.
    local source=CS.System.Activator.CreateInstance(CS.ScriptExecutor.PlayerInfo.CardList:GetType())
    for i=0,original.Count-1 do source:Add(original:get_Item(i)) end
    local chosen=false
    __self:GetDeckUIToAction(tostring(math.min(amount,source.Count)),source,function(cards)
      if chosen or not __active(c) then return end
      chosen=true
      local manager=CS.FightCardManager.Instance
      for i=0,cards.Count-1 do
        local card=cards:get_Item(i)
        manager.cardList:Remove(card); manager.usedCardList:Remove(card)
        __self:CreateCard(card)
      end
      return __waitHand(c,done)
    end)
    return
  end
  if kind=='Discard' or kind=='Burn' then
    if amount==0 or __self.HandCard.Count==0 then return done() end
    local finished=false
    __self:ChooseCardToAction(tostring(amount),function(cards)
      if finished or not __active(c) then return end
      finished=true
      for i=0,cards.Count-1 do
        local card=cards:get_Item(i)
        if kind=='Discard' then card:InternalThrow() else card:InternalBurning() end
      end
      return done()
    end,'0')
    return
  end
  __with(objects,function()
    local v=tostring(amount)
    if kind=='Damage' then __self:Damage(v,'Normal')
    elseif kind=='TrueDamage' then __self:Damage(v,'True')
    elseif kind=='Shield' then __self:ChangeDefence(v)
    elseif kind=='Heal' then __self:ChangeHp(v)
    elseif kind=='MaxHealth' then __self:ChangeMaxHp(v)
    elseif kind=='Energy' then __self:ChangePower(v)
    elseif kind=='Draw' then __self:DrawCount(v)
    elseif kind=='AddBuff' then __self:AddBuff(buff,v)
    elseif kind=='RemoveBuff' then __self:RemoveBuff(buff)
    elseif kind=='Shuffle' then __self:ShuffleDeck()
    elseif kind=='EndTurn' then __self:ChangeRound()
    else error('自建卡牌：未知效果') end
  end)
  if kind=='Draw' then return __waitHand(c,done) end
  return done()
end
local function __listen(event,maximum,perRound,predicate,body)
  local battle=CS.FightManager.Instance
  local count=0
  local roundCount=0
  local busy=false
  -- Each use deliberately creates an independent activation; native disposal ends it with the battle.
  __with(__one(__self.Self),function()
    if perRound>0 then __self:AddEvent('StartRound',function() roundCount=0 end) end
    __self:AddEvent(event,function()
      local c=__context(); c.target=nil
      if busy or c.battle~=battle or not __active(c) or (maximum>0 and count>=maximum) or (perRound>0 and roundCount>=perRound) then return end
      if not predicate(c) then return end
      count=count+1
      roundCount=roundCount+1
      busy=true
      local ok,err=pcall(function() body(c,function() busy=false end) end)
      if not ok then busy=false;error(err) end
    end)
  end)
end
";
}
