// Stand-in for the Workers runtime module, which doesn't exist under Node. Tests use fake bindings
// instead of real Durable Objects, so the base class only needs to exist.
export class DurableObject<Env = unknown> {
  constructor(public ctx: DurableObjectState, public env: Env) {}
}
