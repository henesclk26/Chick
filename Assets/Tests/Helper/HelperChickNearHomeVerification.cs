#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

// Opt-in MCP play verification. No input devices, saved scenes or save files are changed.
public sealed class HelperChickNearHomeVerification : MonoBehaviour
{
    public static string Result = "Not run";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> temporary = new List<GameObject>();
    private readonly List<EdibleObject> food = new List<EdibleObject>();
    private readonly StringBuilder report = new StringBuilder();
    private PlayerUpgrades upgrades;
    private FarmSaveData original;
    private bool background;
    private bool cleaned;
    private int failures;
    private System.Action<EdibleObject> consumeObserver;
    private static object Get(HelperChickController h, string name) => typeof(HelperChickController).GetField(name, Flags).GetValue(h);
    private static void Set(HelperChickController h, string name, object value) => typeof(HelperChickController).GetField(name, Flags).SetValue(h, value);
    private static void Call(HelperChickController h, string name, params object[] args) => typeof(HelperChickController).GetMethod(name, Flags).Invoke(h, args);
    private void Check(bool ok, string message)
    {
        report.AppendLine((ok ? "PASS " : "FAIL ") + message);
        if (!ok) failures++;
    }
    private EdibleObject Seed(Vector3 position)
    {
        var root = new GameObject("NearHomeSeed_TEMP_" + food.Count);
        temporary.Add(root);
        root.layer = 9;
        root.transform.position = position + Vector3.up * .011f;
        var collider = root.AddComponent<SphereCollider>();
        collider.isTrigger = true; collider.radius = .035f;
        var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(visual.GetComponent<Collider>());
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(.025f, .012f, .025f);
        var edible = root.AddComponent<EdibleObject>();
        edible.ConfigureRuntime(root.name, EdibleCategory.Seed, "wheat", "Test seed", root.transform, visual.transform, 1.5f, .06f);
        food.Add(edible);
        return edible;
    }
    private IEnumerator Start()
    {
        Result = "Running";
        background = Application.runInBackground;
        Application.runInBackground = true;
        foreach (var clock in FindObjectsByType<GameTimeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { clock.StopAllCoroutines(); clock.enabled = false; }
        var gate = FindFirstObjectByType<MainMenuGameplayGate>(FindObjectsInactive.Include);
        typeof(MainMenuGameplayGate).GetField("released", Flags).SetValue(gate, true);
        var player = GameObject.Find("ChickPlayer").GetComponent<ChickPlayerController>();
        player.enabled = true;
        player.GetComponent<Animator>().enabled = true;
        player.GetComponent<ChickEatingController>().enabled = false;
        Time.timeScale = 1f;
        player.GetComponent<PlayerGrowthController>().SetForm(PlayerGrowthController.Form.Chicken);
        upgrades = FindFirstObjectByType<PlayerUpgrades>(FindObjectsInactive.Include);
        original = new FarmSaveData();
        typeof(PlayerUpgrades).GetMethod("Capture", Flags).Invoke(upgrades, new object[] { original });
        typeof(PlayerUpgrades).GetMethod("Restore", Flags).Invoke(upgrades, new object[] {
            new FarmSaveData { upgradeLevels = new[] { new UpgradeLevelSaveEntry { id = PlayerUpgrades.HelperChick, level = 3 } } } });
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(500f,299.95f,500f);
        floor.transform.localScale = new Vector3(80f,.1f,80f);
        temporary.Add(floor);
        Vector3 home = new Vector3(500f,300f,500f);
        player.TeleportTo(home + Vector3.up * .01f, Quaternion.identity);
        Physics.SyncTransforms();
        yield return new WaitForSeconds(1f);
        var flock = FindObjectsByType<HelperChickController>(FindObjectsSortMode.None);
        Check(flock.Length == 3, "three live companions");
        if (flock.Length != 3) { Finish(); yield break; }
        float maxIdle = 0f, travel = 0f;
        var previous = new Vector3[3];
        for (int i=0;i<3;i++) previous[i]=flock[i].transform.position;
        for (float t=0;t<6f;t+=Time.deltaTime)
        {
            yield return null;
            for (int i=0;i<3;i++) {
                maxIdle=Mathf.Max(maxIdle, Vector3.Distance(flock[i].transform.position,player.transform.position));
                travel+=Vector3.Distance(previous[i],flock[i].transform.position); previous[i]=flock[i].transform.position;
            }
        }
        Check(maxIdle < 1.1f && travel > .6f, "close independent idle; max="+maxIdle.ToString("F2")+" travel="+travel.ToString("F2"));
        // Place three separate lanes: each helper's nearest seed is unique.
        for (int i=0;i<3;i++) {
            flock[i].enabled=false;
            Vector3 direction=Quaternion.Euler(0,i*120f,0)*Vector3.forward;
            Call(flock[i],"Teleport",home+direction*.55f,Quaternion.LookRotation(direction));
            Seed(home+direction*1.45f);
            Seed(home+direction*1.95f);
        }
        Physics.SyncTransforms();
        var claims = new HashSet<EdibleObject>();
        foreach (var h in flock) h.enabled = true;
        for (int i=0;i<3;i++) {
            Call(flock[i],"FindFood",home,2.2f);
            var target=Get(flock[i],"foodTarget") as EdibleObject;
            Check(target==food[i*2], "helper "+i+" chooses nearest free seed");
            Check(target!=null && claims.Add(target), "unique food claim "+i);
        }
        for (int i=0;i<3;i++) for(int j=i+1;j<3;j++)
            Check(Vector3.Distance((Vector3)Get(flock[i],"standPoint"),(Vector3)Get(flock[j],"standPoint"))>.3f,"separate feeding positions");
        foreach(var h in flock) h.enabled=true;
        float maxForage=0f;
        for(float t=0;t<10f;t+=Time.deltaTime) {
            yield return null;
            foreach(var h in flock) maxForage=Mathf.Max(maxForage,Vector3.Distance(h.transform.position,player.transform.position));
        }
        int consumed=0; foreach(var e in food) if(e.IsConsumed) consumed++;
        Check(consumed>=3,"actual animated consumption="+consumed);
        Check(maxForage>1.1f && maxForage<2.4f,"food-only excursion="+maxForage.ToString("F2"));
        // The owner stays on the ground: climbing must be driven by the food, not by imitation.
        foreach (var e in food) e.gameObject.SetActive(false);
        for (int i=0;i<3;i++) {
            flock[i].enabled=false;
            Call(flock[i],"Teleport",home+Vector3.left*(3f+i),Quaternion.identity);
        }
        var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ledge.name = "ForageLedge_TEMP";
        ledge.transform.position = home+new Vector3(0f,.21f,1.35f);
        ledge.transform.localScale = new Vector3(1.2f,.42f,1.2f);
        temporary.Add(ledge);
        var raisedSeed = Seed(home+new Vector3(0f,.42f,1.5f));
        raisedSeed.ConfigureExposedFace(Vector3.up);
        Call(flock[0],"Teleport",home+Vector3.forward*.4f,Quaternion.identity);
        Set(flock[0],"lastPlayerPosition",player.transform.position);
        flock[0].enabled=true;
        Physics.SyncTransforms();
        bool foodHop=false, keptClaim=false, reachedTop=false, flapped=false;
        float biggestStep=0f;
        Vector3 last=flock[0].transform.position;
        for(float t=0;t<9f;t+=Time.deltaTime) {
            yield return null;
            bool hopping=(bool)Get(flock[0],"hopping");
            foodHop |= hopping;
            keptClaim |= hopping && Get(flock[0],"foodTarget") as EdibleObject == raisedSeed;
            reachedTop |= flock[0].transform.position.y > home.y+.37f && !hopping;
            var animator=flock[0].GetComponent<Animator>();
            flapped |= hopping && (animator.GetCurrentAnimatorStateInfo(1).IsName("wing_flapping") ||
                animator.GetNextAnimatorStateInfo(1).IsName("wing_flapping"));
            biggestStep=Mathf.Max(biggestStep,Vector3.Distance(last,flock[0].transform.position));
            last=flock[0].transform.position;
        }
        Check(foodHop && keptClaim && reachedTop && flapped && raisedSeed.IsConsumed,
            "independent raised food: hop="+foodHop+" reservation="+keptClaim+" landed="+reachedTop+" wings="+flapped+" eaten="+raisedSeed.IsConsumed);
        Check(biggestStep<.35f,"food climb uses physical jump, max frame step="+biggestStep.ToString("F2"));
        ledge.SetActive(false);
        raisedSeed.gameObject.SetActive(false);
        // A too-high target must be skipped rather than walking into its wall forever.
        ledge.transform.position=home+new Vector3(0f,.6f,1.35f);
        ledge.transform.localScale=new Vector3(1.2f,1.2f,1.2f);
        ledge.SetActive(true);
        var unreachableSeed=Seed(home+new Vector3(0f,1.2f,1.5f));
        flock[0].enabled=false;
        Call(flock[0],"Teleport",home+Vector3.forward*.4f,Quaternion.identity);
        Physics.SyncTransforms();
        Call(flock[0],"TryClaim",unreachableSeed);
        Check(Get(flock[0],"foodTarget")==null,"unreachable high food is rejected");
        ledge.SetActive(false);
        unreachableSeed.gameObject.SetActive(false);
        // Only reactivate the original six ground seeds below.
        food.Remove(raisedSeed);
        food.Remove(unreachableSeed);
        // Force a fresh outward trip, then move the owner away smoothly.
        foreach(var e in food) e.ResetForReuse();
        for(int i=0;i<3;i++) {
            var h=flock[i]; h.enabled=false;
            Vector3 direction=Quaternion.Euler(0,i*120f,0)*Vector3.forward;
            Call(h,"Teleport",home+direction*1.25f,Quaternion.LookRotation(direction));
            Set(h,"lastPlayerPosition",player.transform.position);
            Call(h,"TryClaim",food[i*2]);
            Set(h,"lastPlayerPosition",player.transform.position);
        }
        player.TeleportTo(home+Vector3.right*.7f+Vector3.up*.01f,Quaternion.identity);
        Physics.SyncTransforms();
        for(int i=0;i<3;i++) {
            var h=flock[i];
            var reserved=Get(h,"foodTarget");
            Set(h,"playerVelocity",Vector3.right*1.2f);
            Call(h,"UpdateNavigation",player.transform.position,Vector3.Distance(h.transform.position,player.transform.position),2.2f,.2f);
            Call(h,"UpdateFood",player.transform.position,2.2f,Vector3.Distance(h.transform.position,player.transform.position),true,.02f);
            Check(reserved!=null && Get(h,"foodTarget")==reserved && !(bool)Get(h,"following"),
                "walking owner inside forage radius does not interrupt food "+i);
        }
        player.TeleportTo(home+Vector3.right*3.6f+Vector3.up*.01f,Quaternion.identity);
        Physics.SyncTransforms();
        for(int i=0;i<3;i++) {
            var h=flock[i];
            Call(h,"UpdateNavigation",player.transform.position,Vector3.Distance(h.transform.position,player.transform.position),2.2f,.02f);
            Check((bool)Get(h,"following"),"owner outside radius triggers immediate return "+i);
            Call(h,"UpdateFood",player.transform.position,2.2f,Vector3.Distance(h.transform.position,player.transform.position),false,.02f);
            Check(Get(h,"foodTarget")==null,"releases food before return "+i);
        }
        // Re-entering the outer radius must NOT end the trip home.
        player.TeleportTo(home+Vector3.right*.65f+Vector3.up*.01f,Quaternion.identity);
        Physics.SyncTransforms();
        for(int i=0;i<3;i++) {
            var h=flock[i];
            Call(h,"UpdateNavigation",player.transform.position,Vector3.Distance(h.transform.position,player.transform.position),2.2f,.02f);
            Call(h,"UpdateFood",player.transform.position,2.2f,Vector3.Distance(h.transform.position,player.transform.position),true,.3f);
            Check((bool)Get(h,"following") && Get(h,"foodTarget")==null,
                "return stays latched until beside owner, even with food available "+i);
            Set(h,"lastPlayerPosition",player.transform.position);
            h.enabled=true;
        }
        bool premature=false;
        for(float t=0;t<6f;t+=Time.deltaTime) {
            yield return null;
            foreach(var h in flock) if((bool)Get(h,"following") && Get(h,"foodTarget")!=null) premature=true;
        }
        Check(!premature,"no new food claim while returning");
        // Remove the food and verify walking keeps helpers in the near vicinity.
        foreach(var e in food) e.gameObject.SetActive(false);
        // Measure ordinary walking separately from the preceding long food excursion.
        yield return new WaitForSeconds(3f);
        float maxFollow=0f;
        int walkingFollowExits=0;
        float[] slowTime=new float[3], longestSlow=new float[3], speedSum=new float[3];
        int speedSamples=0;
        for(float t=0;t<7f;t+=Time.deltaTime) {
            player.TeleportTo(player.transform.position+Vector3.forward*(1.2f*Time.deltaTime),Quaternion.identity);
            yield return null;
            foreach(var h in flock) maxFollow=Mathf.Max(maxFollow,Vector3.Distance(h.transform.position,player.transform.position));
            if(t>2f) {
                speedSamples++;
                for(int i=0;i<3;i++) {
                    if(!(bool)Get(flock[i],"following")) walkingFollowExits++;
                    Vector3 v=flock[i].GetComponent<CharacterController>().velocity;v.y=0f;
                    speedSum[i]+=v.magnitude;
                    slowTime[i]=v.magnitude<.25f ? slowTime[i]+Time.deltaTime : 0f;
                    longestSlow[i]=Mathf.Max(longestSlow[i],slowTime[i]);
                }
            }
        }
        Check(maxFollow<1.4f,"walking follow stays near; max="+maxFollow.ToString("F2"));
        Check(walkingFollowExits==0,"no follow/idle cycling while owner walks; exits="+walkingFollowExits);
        for(int i=0;i<3;i++) {
            float mean=speedSum[i]/Mathf.Max(1,speedSamples);
            Check(longestSlow[i]<.15f,"no repeated walking pauses helper "+i+" longest="+longestSlow[i].ToString("F3"));
            Check(mean>.9f && mean<1.5f,"matches walking pace helper "+i+" average="+mean.ToString("F2"));
        }
        yield return new WaitForSeconds(.1f);
        foreach(var h in flock) Check((bool)Get(h,"following"),"brief pause retains walking follow");
        yield return new WaitForSeconds(4f);
        foreach(var h in flock) Check(!(bool)Get(h,"following"),"stationary owner releases independent behaviour");
        foreach(var h in flock) Check(Vector3.Distance(h.transform.position,player.transform.position)<1.1f,
            "settles close after walk; gap="+Vector3.Distance(h.transform.position,player.transform.position).ToString("F2"));
        yield return ContinuousFeeding(player, flock, home);
        yield return RealSeedContact(player, flock, home);
        Finish();
    }

    private IEnumerator ContinuousFeeding(ChickPlayerController player, HelperChickController[] flock, Vector3 home)
    {
        player.TeleportTo(home + Vector3.up * .01f, Quaternion.identity);
        foreach (var h in flock) h.enabled = false;
        // 300 seeds exceed the old 128-result scan. All helpers must discover their own closest patch.
        for (int i=0;i<3;i++)
        {
            Vector3 direction=Quaternion.Euler(0f,i*120f,0f)*Vector3.forward;
            Vector3 side=Vector3.Cross(Vector3.up,direction);
            for (int x=0;x<10;x++) for (int z=0;z<10;z++)
                Seed(home+direction*(1.35f+z*.035f)+side*((x-4.5f)*.035f));
            Call(flock[i],"Teleport",home+direction*1.12f,Quaternion.LookRotation(direction));
            Set(flock[i],"lastPlayerPosition",player.transform.position);
        }
        var eater=player.GetComponent<ChickEatingController>();
        eater.enabled=true;
        var ownerAnimator=(Animator)typeof(ChickEatingController).GetField("animator",Flags).GetValue(eater);
        Vector3 contact=player.transform.TransformPoint(eater.LocalImpactPoint);
        for (int i=0;i<35;i++)
            Seed(new Vector3(contact.x+(i%7-3)*.008f,home.y,contact.z+(i/7-2)*.01f));
        Physics.SyncTransforms();
        foreach (var h in flock) h.enabled=true;
        // Exercise the exact SAME food, not just separated feeding patches.
        var sharedSeed=food[food.Count-1];
        Call(flock[0],"Teleport",new Vector3(sharedSeed.BitePosition.x,home.y,sharedSeed.BitePosition.z-.15f),Quaternion.identity);
        Call(flock[0],"TryClaim",sharedSeed);
        var valid=typeof(ChickEatingController).GetMethod("IsTargetValid",Flags);
        Check(Get(flock[0],"foodTarget") as EdibleObject == sharedSeed &&
            !(bool)valid.Invoke(eater,new object[]{sharedSeed}),"player cannot steal an actively reserved helper seed");
        Call(flock[0],"CancelFood");
        Check((bool)valid.Invoke(eater,new object[]{sharedSeed}),"released helper seed becomes selectable for player");
        bool playerClaimed=eater.TryBeginEat();
        if(playerClaimed) Call(flock[0],"TryClaim",eater.CurrentTarget);
        Check(playerClaimed && Get(flock[0],"foodTarget")==null,"helper cannot steal the player's active seed");
        eater.CancelEat();
        Call(flock[0],"Teleport",home+Vector3.forward*1.12f,Quaternion.identity);
        int[] helperMeals=new int[3];
        int playerMeals=0, returnFrames=0, conflictFrames=0;
        var consumedIds=new HashSet<int>();
        bool duplicate=false;
        float maximumBeakGap=0f, maximumFoodSlide=0f;
        var visualPositions=new Dictionary<EdibleObject,Vector3>();
        foreach(var seed in food) if(seed!=null && seed.ContactVisual!=null)
            visualPositions[seed]=seed.ContactVisual.localPosition;
        consumeObserver = edible => {
            if (!consumedIds.Add(edible.GetInstanceID())) duplicate=true;
            if(eater.CurrentTarget==edible) playerMeals++;
            for(int i=0;i<3;i++) if(Get(flock[i],"foodTarget") as EdibleObject == edible) {
                helperMeals[i]++;
                var beak=(Transform)Get(flock[i],"beakEatPoint");
                maximumBeakGap=Mathf.Max(maximumBeakGap,Vector3.Distance(beak.position,edible.BitePosition));
            }
        };
        EdibleObject.Consumed+=consumeObserver;
        float nextOwnerPeck=0f;
        bool stepped=false;
        for(float t=0;t<20f;t+=Time.deltaTime)
        {
            if (!eater.IsBusy) {
                if (!stepped) {
                    // A small step and pause between pecks must not recall all foraging companions.
                    float x=home.x+Mathf.Sin(t*2f)*.08f;
                    player.GetComponent<CharacterController>().Move(Vector3.right*(x-player.transform.position.x));
                    nextOwnerPeck=t+.25f;
                    stepped=true;
                }
                if(t>=nextOwnerPeck && eater.TryBeginEat()) {
                    ownerAnimator.ResetTrigger("jump");
                    ownerAnimator.SetTrigger("eat");
                    stepped=false;
                }
            }
            yield return null;
            var claimed=new HashSet<EdibleObject>();
            if(eater.CurrentTarget!=null) claimed.Add(eater.CurrentTarget);
            foreach(var h in flock) {
                if(t>1f && (bool)Get(h,"following")) returnFrames++;
                var target=Get(h,"foodTarget") as EdibleObject;
                if(target!=null && !claimed.Add(target)) conflictFrames++;
                if(target!=null && target.ContactVisual!=null && visualPositions.TryGetValue(target,out var initial))
                    maximumFoodSlide=Mathf.Max(maximumFoodSlide,Vector3.Distance(initial,target.ContactVisual.localPosition));
            }
        }
        EdibleObject.Consumed-=consumeObserver; consumeObserver=null;
        Check(playerMeals>=5,"owner really eats alongside helpers; meals="+playerMeals);
        Check(maximumBeakGap<=.0121f,"helper consumes only at physical beak contact; max gap="+maximumBeakGap.ToString("F4"));
        Check(maximumFoodSlide<.0001f,"helper food never slides toward beak; max slide="+maximumFoodSlide.ToString("F4"));
        for(int i=0;i<3;i++) {
            Check(helperMeals[i]>=6,"continuous dense feeding helper "+i+" meals="+helperMeals[i]);
            Check(((Collider[])Get(flock[i],"overlaps")).Length>=512,"dense scan grows and reuses capacity helper "+i);
        }
        Check(returnFrames==0,"no return-home trips between bites; following frames="+returnFrames);
        Check(conflictFrames==0 && !duplicate,"exclusive player/helper targets and no duplicate consumption; conflicts="+conflictFrames);
        eater.CancelEat();
        eater.enabled=false;
    }
    private IEnumerator RealSeedContact(ChickPlayerController player, HelperChickController[] flock, Vector3 home)
    {
        foreach(var seed in food) if(seed!=null) seed.gameObject.SetActive(false);
        player.TeleportTo(home+Vector3.up*.01f,Quaternion.identity);
        foreach(var h in flock) h.enabled=false;
        var helper=flock[0];
        string[] names={"CornSeed","SunflowerSeed","RiceGrain","WheatGrain_Test"};
        foreach(string name in names)
        {
            var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Edibles/"+name+".prefab");
            var instance=Instantiate(prefab,home+Vector3.forward*1.25f,Quaternion.Euler(0f,37f,0f));
            instance.name="BeakContact_TEMP_"+name;
            temporary.Add(instance);
            float bottom=float.PositiveInfinity;
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>())
                bottom=Mathf.Min(bottom,renderer.bounds.min.y);
            instance.transform.position+=Vector3.up*(home.y+.001f-bottom);
            var seed=instance.GetComponent<EdibleObject>();
            Vector3 visualStart=seed.ContactVisual.localPosition;
            Vector3 seedStart=seed.transform.position;
            Call(helper,"Teleport",home+Vector3.forward*.9f,Quaternion.identity);
            Set(helper,"lastPlayerPosition",player.transform.position);
            Physics.SyncTransforms();
            Call(helper,"TryClaim",seed);
            Call(helper,"TryBeginEat");
            Check(Get(helper,"eatPhase").ToString()=="None","no remote bite at old suction distance: "+name);
            float gap=float.PositiveInfinity, slide=0f;
            consumeObserver=edible=>{
                if(edible==seed) gap=Vector3.Distance(((Transform)Get(helper,"beakEatPoint")).position,seed.BitePosition);
            };
            EdibleObject.Consumed+=consumeObserver;
            helper.enabled=true;
            for(float t=0;t<5f && !seed.IsConsumed;t+=Time.deltaTime) {
                yield return null;
                slide=Mathf.Max(slide,Vector3.Distance(visualStart,seed.ContactVisual.localPosition));
            }
            Check(seed.IsConsumed && gap<=.0121f,"real prefab contact "+name+" consumed="+seed.IsConsumed+" gap="+gap.ToString("F4"));
            Check(slide<.0001f && Vector3.Distance(seedStart,seed.transform.position)<.0001f,"stationary real seed "+name);
            helper.enabled=false;
            EdibleObject.Consumed-=consumeObserver; consumeObserver=null;
            seed.gameObject.SetActive(false);
        }
    }

    private void Finish() { Result=(failures==0?"PASS\n":"FAIL\n")+report; Debug.Log("NEAR HOME VERIFICATION\n"+Result); Cleanup(); }
    private void Cleanup() {
        if(cleaned) return; cleaned=true;
        if(consumeObserver!=null) EdibleObject.Consumed-=consumeObserver;
        if(upgrades!=null && original!=null) typeof(PlayerUpgrades).GetMethod("Restore",Flags).Invoke(upgrades,new object[]{original});
        foreach(var go in temporary) if(go!=null) Destroy(go);
        Application.runInBackground=background;
    }
    private void OnDestroy() => Cleanup();
}
#endif
