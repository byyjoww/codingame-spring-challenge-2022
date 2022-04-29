using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;

class Player {
    static void Main(string[] args) {
        var mapData = Console.ReadLine();
        var playerRosterData = Console.ReadLine();

        var battlefield = new Battlefield(mapData);
        var playerRoster = new Roster();
        var opponentRoster = new Roster();
        var player = new User(playerRoster);
        var opponent = new User(opponentRoster);
        var heroFactory = new HeroFactory(battlefield, player, opponent);
        var entityFactory = new EntityFactory(battlefield, player, opponent, heroFactory);
        var commandCenter = new CommandCenter(battlefield, player, opponent);


        playerRoster.Add(new List<IHero>() {             
            heroFactory.Defender(0),
            heroFactory.Attacker(1),
            heroFactory.Balanced(2)            
        });

        opponentRoster.Add(new List<IHero>() { 
            heroFactory.Balanced(0),
            heroFactory.Balanced(1),
            heroFactory.Balanced(2)
        });

        while (true)
        {
            player.UpdateData();
            opponent.UpdateData();
            entityFactory.UpdateData();
            commandCenter.IssueCommands();            
        }
    }
}

public class CommandCenter {
    private Battlefield battlefield = default;
    private User player = default;
    private User opponent = default;

    public const int MAX_COMMANDS = 3;
    public static int NumOfCommands { get; set; } = 0;
    public static bool CanIssueCommands => NumOfCommands < MAX_COMMANDS;    
    
    public CommandCenter(Battlefield _battlefield, User _player, User _opponent) {
        this.battlefield = _battlefield;
        this.player = _player;
        this.opponent = _opponent;
    }

    public void IssueCommands()
    {
        NumOfCommands = 0;
        foreach(var hero in player.Roster.Heroes.Values)
        {
            hero.Process();
        }
    }
}

public class Hero : Entity, IHero {
    public ETeam Team { get; set; }
    public int Index { get; private set; }
    public bool Casting { get; private set; }

    // Current State
    public IState CurrentState { get; private set; }

    // Available States
    public IState IdleState { get; private set; }
    public IState FollowState { get; private set; }

    private Battlefield battlefield = default;
    private User player = default;
    private User opponent = default;

    public Hero(int _index, Battlefield _battlefield, User _player, User _opponent, IState _idle, IState _follow) : base() {
        this.Id = -(_index+1);
        this.Index = _index;
        this.IdleState = _idle;
        this.FollowState = _follow;
        this.battlefield = _battlefield;
        this.player = _player;
        this.opponent = _opponent;
        this.Speed = 800f;        
    }

    public void Process() {
        if (CurrentState == null){
            CurrentState = IdleState;
            CurrentState.OnEnter(this);
        }
        Casting = false;
        CurrentState.OnUpdate(this);
    }

    public virtual void Wait() {
        IssueCommand("WAIT nothing to do...");
    }

    public virtual void Move(Vector2Int _pos) {
        IssueCommand($"MOVE {_pos.x} {_pos.y} {_pos}");
    }

    public virtual void Move(IEntity _entity) {
        IssueCommand($"MOVE {_entity.Position.x} {_entity.Position.y} {_entity.GetType().Name}-{_entity.Id}");
    }

    public virtual void Intercept(IEntity _entity) {
        var interceptPointLocation = CalculateInterceptionPoint3D(_entity.Position, _entity.Trajectory);
        Move(interceptPointLocation);
    }

    public virtual void Cast(ISpell _spell) {
        player.Mana -= _spell.Cost;
        Casting = true;
        IssueCommand($"SPELL {_spell.Name} {_spell.Params()} Casting {_spell.Name}");
    }

    public override void UpdateData(string[] _raw) {
        base.UpdateData(_raw);
        Team = (ETeam)int.Parse(_raw[1]);
    }

    public void TransitionToState(IState _state) {
        if (_state == null || _state == CurrentState) { return; }
        CurrentState.OnExit(this);
        CurrentState = _state;
        CurrentState.OnEnter(this);
        CurrentState.OnUpdate(this);
    }

    protected virtual void IssueCommand(string _command){
        if (!CommandCenter.CanIssueCommands)
        { 
            Debug.Log($"[Hero{Id}] Command limit exceeded for round: {CommandCenter.NumOfCommands}/{CommandCenter.MAX_COMMANDS}");
            return; 
        }
        Console.WriteLine(_command);
        CommandCenter.NumOfCommands++;
    }

    public Vector2Int CalculateInterceptionPoint3D(Vector2Int _entityPos, Vector2Int _entityTrajectory) {
         Vector2Int D = Position - _entityPos;
         float d = D.Magnitude;
         float SR = _entityTrajectory.Magnitude;
         float a = MathF.Pow(Speed, 2) - MathF.Pow(SR, 2); 
         float b = 2 * Vector2Int.Dot(D, _entityTrajectory); 
         float c = -Vector2Int.Dot(D, D); 
         if ((MathF.Pow(b, 2) - (4 * (a * c))) < 0)
         {
             return Vector2Int.Zero;
         }
         float t = (-(b) + MathF.Sqrt(MathF.Pow(b, 2) - (4 * (a * c)))) / (2 * a);
         return (((int)t * _entityTrajectory) + _entityPos);
     }
}

public class IdleState : StateBase {
    protected Vector2Int idleSpot = default;    

    public IdleState(Battlefield _battlefield, User _player, User _opponent, int _heroIndex) : base(_battlefield, _player, _opponent, _heroIndex) {
        this.idleSpot = CalculateIdleSpot();
    }

    public override void OnUpdate(IHero _hero)
    {        
        if (battlefield.HasCreatures) {
            _hero.TransitionToState(_hero.FollowState);           
        } else if (!_hero.Position.Equals(idleSpot)) {
            _hero.Move(idleSpot);
        } else {
            _hero.Wait();
        }
    }

    protected virtual Vector2Int CalculateIdleSpot()
    {        
        return new Vector2Int[] 
        {
            Battlefield.IsTopLeft ? new Vector2Int(5000, 900) : new Vector2Int(16000, 4000), // top
            Battlefield.IsTopLeft ? new Vector2Int(4000, 3000) : new Vector2Int(14500, 5000), // middle
            Battlefield.IsTopLeft ? new Vector2Int(1500, 4700) : new Vector2Int(13000, 7000), // bottom
        }[heroIndex];
    }
}

public class FollowState : StateBase {
    protected ICreature target = default;

    public FollowState(Battlefield _battlefield, User _player, User _opponent, int _heroIndex) : base(_battlefield, _player, _opponent, _heroIndex) {
        
    }

    public override void OnUpdate(IHero _hero)
    {
        if (!battlefield.HasCreatures) 
        { 
            _hero.TransitionToState(_hero.IdleState);
            return;
        }

        target = GetHighestPriorityTarget(_hero);
        if (target == null) {
            _hero.TransitionToState(_hero.IdleState); 
            return;
        }        

        if (!target.IsCrowdControlled 
            && !(target.TargettedBy != null && target.TargettedBy.Casting) 
            && player.Mana >= ControlSpell.COST 
            && !target.IsShielded
            && Vector2Int.Distance(target.Position, _hero.Position) <= ControlSpell.RANGE 
            && Vector2Int.Distance(target.Position, Battlefield.PlayerBase) <= 5000
        ) {
            _hero.Cast(new ControlSpell(target, Battlefield.OpponentBase - target.Position));
        } else {
            _hero.Move(target);
        }
    }

    protected bool TargetIsStale(){
        return !battlefield.Creatures.TryGetValue(target.Id, out _);
    }

    protected bool TargetIsTooFar(){
        return target.Position.x > (Battlefield.X_MAX / 2);
    }

    protected virtual ICreature GetHighestPriorityTarget(IHero _hero) {
        var creatures = battlefield.Creatures.Values.ToList();
        creatures.Sort((y, x) => x.ThreatPriority.CompareTo(y.ThreatPriority));
        var first = creatures.First();
        return first.TargettedBy != null && creatures.Count > 1 ? creatures.ElementAt(1) : first;
    }
}

public class DefenderIdleState : IdleState {
    public DefenderIdleState(Battlefield _battlefield, User _player, User _opponent, int _heroIndex) : base(_battlefield, _player, _opponent, _heroIndex) {
        
    }

    public override void OnUpdate(IHero _hero)
    {
        if (battlefield.HasCreatures && HasCriticalCreatures(_hero)) {
            _hero.TransitionToState(_hero.FollowState); 
        } else if (!_hero.Position.Equals(idleSpot)) {
            _hero.Move(idleSpot);
        } else {
            _hero.Wait();
        }        
    }

    protected override Vector2Int CalculateIdleSpot()
    {
        return Battlefield.IsTopLeft ? new Vector2Int(Battlefield.X_MIN+2000, Battlefield.Y_MIN+2000) : new Vector2Int(Battlefield.X_MAX-2000, Battlefield.Y_MAX-2000);
    }

    private bool HasCriticalCreatures(IHero _hero) {
        return battlefield.Creatures.Values
            .Where(x => x.ThreatPriority > 1 && Vector2Int.Distance(Battlefield.PlayerBase, x.Position) < 5000)
            .Count() > 0;
    }
}

public class DefenderFollowState : FollowState {
    public DefenderFollowState(Battlefield _battlefield, User _player, User _opponent, int _heroIndex) : base(_battlefield, _player, _opponent, _heroIndex) {
        
    }

    public override void OnUpdate(IHero _hero)
    {
        if (!battlefield.HasCreatures) 
        { 
            _hero.TransitionToState(_hero.IdleState);
            return;
        }

        target = GetHighestPriorityTarget(_hero);
        if (target == null) {
            _hero.TransitionToState(_hero.IdleState); 
            return;
        }     

        if (Vector2Int.Distance(target.Position, Battlefield.PlayerBase) < 4000 
            && !target.IsCrowdControlled 
            && !target.IsShielded
            && !(target.TargettedBy != null && target.TargettedBy.Casting) 
            && player.Mana >= WindSpell.COST 
            && Vector2Int.Distance(target.Position, _hero.Position) <= WindSpell.RANGE
            && IsTowardsOpponentBase(_hero, target)
        ) {
            _hero.Cast(new WindSpell(Battlefield.OpponentBase - _hero.Position));
        } else {
            _hero.Move(target);
        }
    }

    protected override ICreature GetHighestPriorityTarget(IHero _hero) {
        return battlefield.Creatures.Values
            .Where(x => x.ThreatPriority > 1 && Vector2Int.Distance(Battlefield.PlayerBase, x.Position) < 5000)
            .OrderByDescending(x => x.ThreatPriority)
            .FirstOrDefault();
    }

    private bool IsTowardsOpponentBase(IHero _hero, IEntity _target){
        Vector2Int dir = _target.Position - _hero.Position;
        float direction = Vector2Int.Dot (dir, Battlefield.OpponentBase - _hero.Position);
        return direction < 0;
    }
}

public class AttackerIdleState : IdleState {
    public AttackerIdleState(Battlefield _battlefield, User _player, User _opponent, int _heroIndex) : base(_battlefield, _player, _opponent, _heroIndex) {
        
    }

    public override void OnUpdate(IHero _hero)
    {        
        if (battlefield.HasCreatures && HasImportantCreatures(_hero)) {
            _hero.TransitionToState(_hero.FollowState);           
        } else if (!_hero.Position.Equals(idleSpot)) {
            _hero.Move(idleSpot);
        } else {
            _hero.Wait();
        }
    }

    protected override Vector2Int CalculateIdleSpot()
    {
        return new Vector2Int(Battlefield.X_MAX/2, Battlefield.Y_MAX/2);
    }

    private bool HasImportantCreatures(IHero _hero) {
        return battlefield.Creatures.Values
            .Where(x => x.ThreatPriority > 0 && Vector2Int.Distance(new Vector2Int(Battlefield.X_MAX/2, Battlefield.Y_MAX/2), x.Position) < 5000)
            .OrderBy(x => Vector2Int.Distance(_hero.Position, x.Position))
            .Count() > 0;
    }
}

public class AttackerFollowState : FollowState {
    public AttackerFollowState(Battlefield _battlefield, User _player, User _opponent, int _heroIndex) : base(_battlefield, _player, _opponent, _heroIndex) {
        
    }

    public override void OnUpdate(IHero _hero)
    {
        if (!battlefield.HasCreatures) 
        { 
            _hero.TransitionToState(_hero.IdleState);
            return;
        }        

        target = GetHighestPriorityTarget(_hero);
        if (target == null) {
            _hero.TransitionToState(_hero.IdleState); 
            return;
        }  

        if (HasMoreThanXWindTargetsTowardsTheEnemy(5)) {
            _hero.Cast(new WindSpell(Battlefield.OpponentBase - _hero.Position));
        } else {
            _hero.Move(target);
        }
    }

    protected override ICreature GetHighestPriorityTarget(IHero _hero) {
        return battlefield.Creatures.Values
            .Where(x => x.ThreatPriority > 0 && Vector2Int.Distance(new Vector2Int(Battlefield.X_MAX/2, Battlefield.Y_MAX/2), x.Position) < 5000)
            .OrderBy(x => Vector2Int.Distance(_hero.Position, x.Position))
            .FirstOrDefault();
    }

    protected bool CastSpellOnRandomCreature(IHero _hero) {
        var castTarget = battlefield.Creatures.Values
            .Where(x => x.ThreatPriority > 0 && Vector2Int.Distance(x.Position, _hero.Position) <= ControlSpell.RANGE && x.HealthPercent > 0.5f)            
            .OrderBy(x => x.ThreatPriority)
            .FirstOrDefault();
        if (castTarget != null 
            && !castTarget.IsCrowdControlled 
            && !target.ShieldRounds
            && !(castTarget.TargettedBy != null && castTarget.TargettedBy.Casting) 
            && castTarget.ThreatPriority != -1 
            && player.Mana >= ControlSpell.COST 
            && Vector2Int.Distance(castTarget.Position, _hero.Position) <= ControlSpell.RANGE
        ) {
            _hero.Cast(new ControlSpell(castTarget, Battlefield.OpponentBase - castTarget.Position));
            return true;
        }
        return false;
    }

    protected bool HasMoreThanXWindTargetsTowardsTheEnemy(int _requiredTargets){
        return false;
    }
}

public interface ISpell {
    string Name { get; }
    int Range { get; }
    int Cost { get; }

    string Params();
}

public class WindSpell : ISpell{
    public const string NAME = "WIND";
    public const int RANGE = 1280;
    public const int COST = 10;

    public string Name => NAME;
    public int Range => RANGE;
    public int Cost => COST;

    private Vector2Int direction = default;

    public WindSpell(Vector2Int _direction){
        this.direction = _direction;
    }

    public string Params()
    {
        return $"{direction.x} {direction.y}";
    }
}

public class ControlSpell : ISpell{
    public const string NAME = "CONTROL";
    public const int RANGE = 2200;
    public const int COST = 10;

    private IEntity entity = default;
    private Vector2Int direction = default;

    public string Name => NAME;
    public int Range => RANGE;
    public int Cost => COST;

    public ControlSpell(IEntity _entity, Vector2Int _direction){
        this.entity = _entity;
        this.direction = _direction;
    }

    public string Params()
    {
        return $"{entity.Id} {direction.x} {direction.y}";
    }
}

public class ShieldSpell : ISpell{
    public const string NAME = "SHIELD";
    public const int RANGE = 2200;
    public const int COST = 10;

    private IEntity entity = default;

    public string Name => NAME;
    public int Range => RANGE;
    public int Cost => COST;

    public ShieldSpell(IEntity _entity){
        this.entity = _entity;
    }

    public string Params()
    {
        return $"{entity.Id}";
    }
}

public class Battlefield {
    public const int X_MIN = 0;
    public const int X_MAX = 17630;
    public const int Y_MIN = 0;
    public const int Y_MAX = 9000;
    public const float DISTANCE_BETWEEN_BASES = 19794.365f;

    public static Vector2Int PlayerBase { get; private set; }
    public static Vector2Int OpponentBase { get; private set; }

    public Dictionary<int, ICreature> Creatures { get; private set; }
    public bool HasCreatures => Creatures.Count > 0;
    public static bool IsTopLeft => PlayerBase.x < (X_MAX/2);

    public Battlefield(string _mapData)
    {
        var coordinates = _mapData.Split(' ');
        PlayerBase = new Vector2Int(int.Parse(coordinates[0]), int.Parse(coordinates[1]));
        OpponentBase = new Vector2Int(IsTopLeft ? X_MAX : X_MIN, IsTopLeft ? Y_MAX : Y_MIN);        
        Creatures = new Dictionary<int, ICreature>();
    }
}

public class User {
    public int Health { get; set; }
    public int Mana { get; set; } // Ignore in the first league; Spend ten mana to cast a spell
    public Roster Roster { get; private set; }

    public User(Roster _roster)
    {
        this.Roster = _roster;
    }

    public void UpdateData()
    {
        var resources = Console.ReadLine().Split(' ');
        Health = int.Parse(resources[0]);
        Mana = int.Parse(resources[1]);
    }
}

public class Roster {
    public Dictionary<int, IHero> Heroes { get; set; }

    public Roster()
    {
        Heroes = new Dictionary<int, IHero>();
    }

    public void Add(IEnumerable<IHero> _heroes){
        foreach(var hero in _heroes)
        {
            Heroes.Add(hero.Id, hero);
        }
    }
}

public class EntityFactory {
    private Battlefield battlefield = default;
    private User player = default;
    private User opponent = default;
    private HeroFactory heroFactory = default;
    private Dictionary<string, EEntityType> turnEntities = new Dictionary<string, EEntityType>();

    public EntityFactory(Battlefield _battlefield, User _player, User _opponent, HeroFactory _heroFactory)
    {
        this.battlefield = _battlefield;
        this.player = _player;
        this.opponent = _opponent;
        this.heroFactory = _heroFactory;
    }

    public void UpdateData()
    {
        turnEntities.Clear();
        int entityCount = int.Parse(Console.ReadLine()); // Amount of heros and monsters you can see
        for (int i = 0; i < entityCount; i++)
        {
            var entityData = Console.ReadLine();
            ParseEntity(entityData);            
        }

        for (int i = battlefield.Creatures.Count - 1; i >= 0; i--)
        {
            var kvp = battlefield.Creatures.ElementAt(i);
            string key = $"{kvp.Key}{kvp.Value.EntityType}";
            if (!turnEntities.TryGetValue(key, out EEntityType _type)) {
                battlefield.Creatures.Remove(kvp.Key);
            }            
        }
    }

    private void ParseEntity(string _entityData) {
        string[] raw = _entityData.Split(' ');
        var type = (EEntityType)int.Parse(raw[1]);
        var id = int.Parse(raw[0]);
        turnEntities.Add($"{id}{type}", type);

        if (type == EEntityType.Creature){
            ParseCreature(id, raw);
        } else {
            ParseHero(id, type, raw);
        }        
    }

    private void ParseCreature(int _id, string[] _raw){
        if (battlefield.Creatures.TryGetValue(_id, out ICreature _creature)){
            _creature.UpdateData(_raw);
        } else {
            battlefield.Creatures.Add(_id, new Creature(_raw));
        }
    }

    private void ParseHero(int _id, EEntityType _type, string[] _raw){
        Roster roster = _type == EEntityType.PlayerHero ? player.Roster : opponent.Roster;
        if (roster.Heroes.TryGetValue(_id, out IHero _hero)){
            _hero.UpdateData(_raw);
        } else {
            var negativeHeroKvp = roster.Heroes.FirstOrDefault(x => x.Value.Id < 0);
            if (negativeHeroKvp.Value is null) {
                throw new System.Exception("attempting to add a new hero");
            } else {
                IHero hero = roster.Heroes[negativeHeroKvp.Key];
                hero.UpdateData(_raw);
                roster.Heroes.Remove(negativeHeroKvp.Key);
                roster.Heroes.Add(_id, hero);
            }
        }
    }
}

public class HeroFactory {
    private Battlefield battlefield = default;
    private User player = default;
    private User opponent = default;

    public HeroFactory(Battlefield _battlefield, User _player, User _opponent){
        this.battlefield = _battlefield;
        this.player = _player;
        this.opponent = _opponent;
    }

    public IHero Balanced(int _index){
        return new Hero(
            _index, 
            battlefield,
            player,
            opponent,
            new IdleState(battlefield, player, opponent, _index),
            new FollowState(battlefield, player, opponent, _index)
       );
    }

    public IHero Defender(int _index){
        return new Hero(
            _index, 
            battlefield,
            player,
            opponent,
            new DefenderIdleState(battlefield, player, opponent, _index),
            new DefenderFollowState(battlefield, player, opponent, _index)
       );
    }

    public IHero Attacker(int _index){
        return new Hero(
            _index, 
            battlefield,
            player,
            opponent,
            new AttackerIdleState(battlefield, player, opponent, _index),
            new AttackerFollowState(battlefield, player, opponent, _index)
       );
    }
}

public abstract class Entity : IEntity {
    public EEntityType EntityType { get; protected set; }
    public int Id { get; protected set; }    
    public int Health { get; protected set; }
    public int ShieldRounds { get; protected set; }
    public bool IsCrowdControlled { get; protected set; }
    public Vector2Int Position { get; protected set; }
    public Vector2Int Trajectory { get; protected set; }
    public float Speed { get; protected set; }
    public bool IsShielded => ShieldRounds > 0;

    public Entity() {

    }

    public Entity(string[] _raw) {
        UpdateData(_raw);
    }

    public virtual void UpdateData(string[] _raw) {    
        EntityType = (EEntityType)int.Parse(_raw[1]);    
        Id = int.Parse(_raw[0]);
        Health = int.Parse(_raw[6]);
        ShieldRounds = int.Parse(_raw[4]);
        IsCrowdControlled = int.Parse(_raw[5]) == 1;
        Position = new Vector2Int(int.Parse(_raw[2]), int.Parse(_raw[3]));
        Trajectory = new Vector2Int(int.Parse(_raw[7]), int.Parse(_raw[8]));        
    }
}

public class Creature : Entity, ICreature {
    public EThreatTo ThreatTo { get; private set; }
    public bool HasBaseTarget { get; private set; }
    public float ThreatPriority { get; private set; }
    public IHero TargettedBy { get; private set; }
    public int MaxHealth { get; private set; }
    public float HealthPercent => (float)Health / (float)MaxHealth;

    public Creature(string[] _raw) : base(_raw)
    {
        this.MaxHealth = int.Parse(_raw[6]);
        this.Speed = 400f;
    }

    public void CalculateThreatPriority() {
        if (ThreatTo == EThreatTo.Opponent) 
        { 
            ThreatPriority = -1; 
            return;
        }
        
        var distanceToPlayerBase = Vector2Int.Distance(Battlefield.PlayerBase, Position);
		var distanceNormalized = (distanceToPlayerBase - 0) / (Battlefield.DISTANCE_BETWEEN_BASES - 0);	
        ThreatPriority = (float)ThreatTo + (1 - distanceNormalized);
    }

    public override void UpdateData(string[] _raw) {
        base.UpdateData(_raw);
        HasBaseTarget = int.Parse(_raw[9]) == 1;
        ThreatTo = (EThreatTo)int.Parse(_raw[10]);
        CalculateThreatPriority();
    }
}

public interface IEntity {
    EEntityType EntityType { get; }
    int Id { get; }
    int Health { get; }
    int ShieldRounds { get; } // Ignore for this league; Count down until shield spell fades
    bool IsCrowdControlled { get; } // Ignore for this league; Equals 1 when this entity is under a control spell
    Vector2Int Position { get; }
    Vector2Int Trajectory { get; }
    float Speed { get; }
    bool IsShielded { get; }
    
    void UpdateData(string[] raw);
}

public interface IHero : IEntity {
    ETeam Team { get; } 
    int Index { get; }
    bool Casting { get; }

    IState IdleState { get; }
    IState FollowState { get; }

    void Wait();
    void Move(Vector2Int _pos);
    void Move(IEntity _entity);
    void Intercept(IEntity _entity);
    void Cast(ISpell _spell);
    void Process();
    void TransitionToState(IState _state);
}

public interface ICreature : IEntity {
    EThreatTo ThreatTo { get; }
    bool HasBaseTarget { get; }
    float ThreatPriority { get; }
    IHero TargettedBy { get; }
    int MaxHealth { get; }
    float HealthPercent { get; }
}

public interface IState {
    void OnEnter(IHero _hero);
    void OnUpdate(IHero _hero);
    void OnExit(IHero _hero);
}

public abstract class StateBase : IState {
    protected Battlefield battlefield { get; private set; }
    protected User player { get; private set; }
    protected User opponent { get; private set; }
    protected int heroIndex { get; private set; }

    public StateBase(Battlefield _battlefield, User _player, User _opponent, int _heroIndex) {
        this.battlefield = _battlefield;
        this.player = _player;
        this.opponent = _opponent;
        this.heroIndex = _heroIndex;
    }

    public virtual void OnEnter(IHero _hero){ 

    }

    public virtual void OnExit(IHero _hero){

    }

    public virtual void OnUpdate(IHero _hero){

    }
}

public enum EEntityType {
    Creature = 0,
    PlayerHero = 1,
    OpponentHero = 2,
}

public enum ETeam {
    Neutral = 0,
    Player = 1,
    Opponent = 2,
}

public enum EThreatTo{
    None = 0,
    Player = 1,
    Opponent = 2,
}

public struct Vector2Int {
    public int x { get; private set; }
    public int y { get; private set; }

    public float Magnitude => MathF.Sqrt((float)(x * x + y * y));
    public int SqrMagnitude => x * x + y * y;

    private static readonly Vector2Int s_Zero = new Vector2Int(0, 0);
    private static readonly Vector2Int s_One = new Vector2Int(1, 1);
    private static readonly Vector2Int s_Up = new Vector2Int(0, 1);
    private static readonly Vector2Int s_Down = new Vector2Int(0, -1);
    private static readonly Vector2Int s_Left = new Vector2Int(-1, 0);
    private static readonly Vector2Int s_Right = new Vector2Int(1, 0);

    public static Vector2Int Zero { get { return s_Zero; } }
    public static Vector2Int One { get { return s_One; } }
    public static Vector2Int Up { get { return s_Up; } }
    public static Vector2Int Down { get { return s_Down; } }
    public static Vector2Int Left { get { return s_Left; } }
    public static Vector2Int Right { get { return s_Right; } }

    public Vector2Int(int _x, int _y){
        this.x = _x;
        this.y = _y;
    }

    public static Vector2Int operator-(Vector2Int v)
    {
        return new Vector2Int(-v.x, -v.y);
    }

    public static Vector2Int operator+(Vector2Int a, Vector2Int b)
    {
        return new Vector2Int(a.x + b.x, a.y + b.y);
    }

    public static Vector2Int operator-(Vector2Int a, Vector2Int b)
    {
        return new Vector2Int(a.x - b.x, a.y - b.y);
    }

    public static Vector2Int operator*(Vector2Int a, Vector2Int b)
    {
        return new Vector2Int(a.x * b.x, a.y * b.y);
    }

    public static Vector2Int operator*(int a, Vector2Int b)
    {
        return new Vector2Int(a * b.x, a * b.y);
    }

    public static Vector2Int operator*(Vector2Int a, int b)
    {
        return new Vector2Int(a.x * b, a.y * b);
    }

    public static Vector2Int operator/(Vector2Int a, int b)
    {
        return new Vector2Int(a.x / b, a.y / b);
    }

    public static bool operator==(Vector2Int lhs, Vector2Int rhs)
    {
        return lhs.x == rhs.x && lhs.y == rhs.y;
    }

    public static bool operator!=(Vector2Int lhs, Vector2Int rhs)
    {
        return !(lhs == rhs);
    }

    public static float Distance(Vector2Int a, Vector2Int b)
    {
        float diff_x = a.x - b.x;
        float diff_y = a.y - b.y;

        return (float)Math.Sqrt(diff_x * diff_x + diff_y * diff_y);
    }

    public static float Dot(Vector2Int lhs, Vector2Int rhs) 
    { 
        return lhs.x * rhs.x + lhs.y * rhs.y; 
    }

    public override bool Equals(object other)
    {
        if (!(other is Vector2Int)) return false;
        return Equals((Vector2Int)other);
    }

    public bool Equals(Vector2Int other)
    {
        return x == other.x && y == other.y;
    }

    public override int GetHashCode()
    {
        return x.GetHashCode() ^ (y.GetHashCode() << 2);
    }

    public override string ToString()
    {
        return string.Format($"({x}, {y})");
    }
}

public static class Debug {
    public static void Log(string _msg)
    {
        Console.Error.WriteLine(_msg);
    }
}

public static class IEnumerableExtensions{
    public static Random rand = new Random();

    public static T Random<T>(this IEnumerable<T> _collection){
        int r = rand.Next(0, _collection.Count());
        return _collection.ElementAt(r);
    }
}