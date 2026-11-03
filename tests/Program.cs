using CSGOFeel.Movement;
int assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); ++assertions; }
bool Stop(bool alive=true, bool ground=true, bool walk=true, bool input=true, bool moving=false, bool jump=false, float x=250, float y=0)
    => MovementPolicy.ShouldStop(alive,ground,walk,input,moving,jump,x,y);
Check(Stop(), "released grounded movement stops immediately");
Check(!Stop(moving:true), "held movement preserved");
Check(!Stop(ground:false), "air velocity preserved");
Check(!Stop(jump:true), "jump preserved");
Check(!Stop(walk:false), "ladder/noclip preserved");
Check(!Stop(alive:false), "dead player ignored");
Check(!Stop(input:false), "unknown input fails closed");
Check(!Stop(x:float.NaN), "NaN rejected");
Check(!Stop(x:float.PositiveInfinity), "infinity rejected");
Check(!Stop(x:0), "stationary player not rewritten");
Check(!Stop(x:500), "high external impulse preserved");
for (int i=1;i<=350;i++) {
 Check(Stop(x:i), "release speed " + i);
 Check(!Stop(moving:true,x:i), "held speed " + i);
}
Console.WriteLine($"PASS: {assertions} movement policy assertions. Engine latency NOT measured.");
