import './ConfigurationPortal.css';

const emptyConfig = {
  endpoints: [],
  councilModelKeys: [],
  chairmanModelKey: '',
};

const normalizeConfig = (config) => ({
  endpoints: config?.endpoints ?? [],
  councilModelKeys: config?.councilModelKeys ?? config?.council_model_keys ?? [],
  chairmanModelKey:
    config?.chairmanModelKey ?? config?.chairman_model_key ?? '',
});

const groupModelsByEndpoint = (models) =>
  models.reduce((acc, model) => {
    const endpointId = model.endpointId ?? model.endpoint_id;
    if (!acc[endpointId]) acc[endpointId] = [];
    acc[endpointId].push(model);
    return acc;
  }, {});

const labelForModel = (model) => {
  const displayName = model.displayName ?? model.display_name;
  const modelId = model.modelId ?? model.model_id;
  const endpointName = model.endpointName ?? model.endpoint_name;
  return `${displayName || modelId} · ${endpointName}`;
};

export default function ConfigurationPortal({
  configuration,
  discoveredModels,
  isLoading,
  isSaving,
  onConfigurationChange,
  onSave,
  onRefresh,
  onClose,
}) {
  const config = normalizeConfig(configuration ?? emptyConfig);
  const grouped = groupModelsByEndpoint(discoveredModels ?? []);

  const addEndpoint = () => {
    onConfigurationChange({
      ...config,
      endpoints: [
        ...config.endpoints,
        {
          id: crypto.randomUUID().replaceAll('-', ''),
          name: 'New Endpoint',
          modelsUrl: 'http://localhost:3000/v1/models',
          apiKey: '',
          enabled: true,
        },
      ],
    });
  };

  const updateEndpoint = (index, patch) => {
    onConfigurationChange({
      ...config,
      endpoints: config.endpoints.map((endpoint, i) =>
        i === index ? { ...endpoint, ...patch } : endpoint
      ),
    });
  };

  const removeEndpoint = (endpointId) => {
    const nextModelKeys = config.councilModelKeys.filter(
      (key) => !key.startsWith(`${endpointId}::`)
    );

    const nextChairman = config.chairmanModelKey.startsWith(`${endpointId}::`)
      ? ''
      : config.chairmanModelKey;

    onConfigurationChange({
      ...config,
      endpoints: config.endpoints.filter((e) => e.id !== endpointId),
      councilModelKeys: nextModelKeys,
      chairmanModelKey: nextChairman,
    });
  };

  const toggleCouncilModel = (modelKey) => {
    const selected = new Set(config.councilModelKeys);
    if (selected.has(modelKey)) {
      selected.delete(modelKey);
    } else {
      selected.add(modelKey);
    }

    const updatedKeys = Array.from(selected);
    const chairmanStillValid = updatedKeys.includes(config.chairmanModelKey);

    onConfigurationChange({
      ...config,
      councilModelKeys: updatedKeys,
      chairmanModelKey: chairmanStillValid ? config.chairmanModelKey : '',
    });
  };

  return (
    <div className="configuration-portal">
      <div className="configuration-header">
        <div>
          <h2>Council Builder</h2>
          <p>Configure endpoints, discover models, and compose your council.</p>
        </div>
        <div className="configuration-actions">
          <button onClick={onRefresh} disabled={isLoading || isSaving}>
            Refresh Models
          </button>
          <button
            className="primary"
            onClick={onSave}
            disabled={isLoading || isSaving}
          >
            {isSaving ? 'Saving...' : 'Save Configuration'}
          </button>
          <button onClick={onClose}>Back to Chat</button>
        </div>
      </div>

      <div className="configuration-columns">
        <section className="config-panel endpoints-panel">
          <div className="panel-header-row">
            <h3>1) Endpoints</h3>
            <button onClick={addEndpoint}>+ Add Endpoint</button>
          </div>

          {config.endpoints.length === 0 ? (
            <p className="panel-empty">No endpoints configured yet.</p>
          ) : (
            config.endpoints.map((endpoint, index) => (
              <div key={endpoint.id} className="endpoint-card">
                <div className="endpoint-card-header">
                  <strong>{endpoint.name || 'Endpoint'}</strong>
                  <button onClick={() => removeEndpoint(endpoint.id)}>Remove</button>
                </div>

                <label>
                  Name
                  <input
                    value={endpoint.name}
                    onChange={(e) =>
                      updateEndpoint(index, { name: e.target.value })
                    }
                  />
                </label>

                <label>
                  OpenWebUI /v1/models URL
                  <input
                    value={endpoint.modelsUrl ?? endpoint.models_url ?? ''}
                    onChange={(e) =>
                      updateEndpoint(index, { modelsUrl: e.target.value })
                    }
                    placeholder="https://host/v1/models"
                  />
                </label>

                <label>
                  API Key (optional)
                  <input
                    type="password"
                    value={endpoint.apiKey ?? endpoint.api_key ?? ''}
                    onChange={(e) =>
                      updateEndpoint(index, { apiKey: e.target.value })
                    }
                  />
                </label>

                <label className="checkbox-row">
                  <input
                    type="checkbox"
                    checked={endpoint.enabled ?? true}
                    onChange={(e) =>
                      updateEndpoint(index, { enabled: e.target.checked })
                    }
                  />
                  Enabled
                </label>
              </div>
            ))
          )}
        </section>

        <section className="config-panel models-panel">
          <h3>2) Discovered Models</h3>

          {(discoveredModels ?? []).length === 0 ? (
            <p className="panel-empty">
              No models discovered. Save or refresh after configuring valid endpoints.
            </p>
          ) : (
            config.endpoints.map((endpoint) => (
              <div key={endpoint.id} className="model-group">
                <h4>{endpoint.name || 'Endpoint'}</h4>
                <div className="model-list">
                  {(grouped[endpoint.id] ?? []).map((model) => {
                    const modelKey = model.key;
                    const checked = config.councilModelKeys.includes(modelKey);
                    return (
                      <label key={modelKey} className="checkbox-row model-checkbox">
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={() => toggleCouncilModel(modelKey)}
                        />
                        <span>{labelForModel(model)}</span>
                      </label>
                    );
                  })}
                </div>
              </div>
            ))
          )}
        </section>

        <section className="config-panel hierarchy-panel">
          <h3>3) Council Hierarchy</h3>
          <p className="panel-empty">Select a chairman from chosen council members.</p>

          <label>
            Chairman
            <select
              value={config.chairmanModelKey}
              onChange={(e) =>
                onConfigurationChange({
                  ...config,
                  chairmanModelKey: e.target.value,
                })
              }
            >
              <option value="">Select chairman model</option>
              {config.councilModelKeys.map((key) => {
                const model = (discoveredModels ?? []).find((m) => m.key === key);
                return (
                  <option key={key} value={key}>
                    {model ? labelForModel(model) : key}
                  </option>
                );
              })}
            </select>
          </label>

          <div className="hierarchy-visual">
            <div className="hierarchy-chair">
              <div className="hierarchy-title">Chairman</div>
              <div className="hierarchy-node-value">
                {config.chairmanModelKey || 'Not selected'}
              </div>
            </div>
            <div className="hierarchy-divider" />
            <div>
              <div className="hierarchy-title">Council Members ({config.councilModelKeys.length})</div>
              <div className="member-tags">
                {config.councilModelKeys.length === 0 ? (
                  <span className="tag">No members selected</span>
                ) : (
                  config.councilModelKeys.map((key) => <span key={key} className="tag">{key}</span>)
                )}
              </div>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
