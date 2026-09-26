# Performance measurements

No Unity Editor/runtime is installed in this workspace. The requested 20k-cell orbit frame-rate and ≤5 s terrain generation benchmarks remain **unmeasured**. Generation currently runs synchronously on scene start and must be converted to scheduled jobs before claiming the stage-02 budget. Level 5 generates 10,242 primal cells; level 6 generates 40,962.
