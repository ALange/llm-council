import { useState, useEffect, useCallback } from 'react';
import Sidebar from './components/Sidebar';
import ChatInterface from './components/ChatInterface';
import ConfigurationPortal from './components/ConfigurationPortal';
import { api } from './api';
import './App.css';


const normalizePortalConfig = (config) => ({
  endpoints: (config?.endpoints ?? []).map((endpoint) => ({
    id: endpoint.id,
    name: endpoint.name ?? '',
    modelsUrl: endpoint.modelsUrl ?? endpoint.models_url ?? '',
    apiKey: endpoint.apiKey ?? endpoint.api_key ?? '',
    enabled: endpoint.enabled ?? true,
  })),
  councilModelKeys: config?.councilModelKeys ?? config?.council_model_keys ?? [],
  chairmanModelKey: config?.chairmanModelKey ?? config?.chairman_model_key ?? '',
});

function App() {
  const [conversations, setConversations] = useState([]);
  const [currentConversationId, setCurrentConversationId] = useState(null);
  const [currentConversation, setCurrentConversation] = useState(null);
  const [isLoading, setIsLoading] = useState(false);
  const [viewMode, setViewMode] = useState('chat');

  const [configuration, setConfiguration] = useState({
    endpoints: [],
    councilModelKeys: [],
    chairmanModelKey: '',
  });
  const [discoveredModels, setDiscoveredModels] = useState([]);
  const [isConfigLoading, setIsConfigLoading] = useState(false);
  const [isConfigSaving, setIsConfigSaving] = useState(false);

  const loadConversations = useCallback(async () => {
    try {
      const convs = await api.listConversations();
      setConversations(convs);
    } catch (error) {
      console.error('Failed to load conversations:', error);
    }
  }, []);

  const loadConversation = useCallback(async (id) => {
    try {
      const conv = await api.getConversation(id);
      setCurrentConversation(conv);
    } catch (error) {
      console.error('Failed to load conversation:', error);
    }
  }, []);

  const loadConfiguration = useCallback(async () => {
    setIsConfigLoading(true);
    try {
      const response = await api.getConfiguration();
      setConfiguration(normalizePortalConfig(response.config));
      setDiscoveredModels(response.discoveredModels ?? response.discovered_models ?? []);
    } catch (error) {
      console.error('Failed to load configuration:', error);
    } finally {
      setIsConfigLoading(false);
    }
  }, []);

  useEffect(() => {
    loadConversations();
    loadConfiguration();
  }, [loadConversations, loadConfiguration]);

  useEffect(() => {
    if (currentConversationId) {
      loadConversation(currentConversationId);
    }
  }, [currentConversationId, loadConversation]);

  const handleNewConversation = async () => {
    try {
      const newConv = await api.createConversation();
      setConversations((prev) => [
        { id: newConv.id, created_at: newConv.created_at, message_count: 0 },
        ...prev,
      ]);
      setCurrentConversationId(newConv.id);
      setViewMode('chat');
    } catch (error) {
      console.error('Failed to create conversation:', error);
    }
  };

  const handleSelectConversation = (id) => {
    setCurrentConversationId(id);
    setViewMode('chat');
  };

  const handleSaveConfiguration = async () => {
    setIsConfigSaving(true);
    try {
      const response = await api.saveConfiguration(normalizePortalConfig(configuration));
      setConfiguration(normalizePortalConfig(response.config));
      setDiscoveredModels(response.discoveredModels ?? response.discovered_models ?? []);
    } catch (error) {
      console.error('Failed to save configuration:', error);
    } finally {
      setIsConfigSaving(false);
    }
  };

  const handleSendMessage = async (content) => {
    if (!currentConversationId) return;

    setIsLoading(true);
    try {
      // Optimistically add user message to UI
      const userMessage = { role: 'user', content };
      setCurrentConversation((prev) => ({
        ...prev,
        messages: [...prev.messages, userMessage],
      }));

      // Create a partial assistant message that will be updated progressively
      const assistantMessage = {
        role: 'assistant',
        stage1: null,
        stage2: null,
        stage3: null,
        metadata: null,
        loading: {
          stage1: false,
          stage2: false,
          stage3: false,
        },
      };

      // Add the partial assistant message
      setCurrentConversation((prev) => ({
        ...prev,
        messages: [...prev.messages, assistantMessage],
      }));

      // Send message with streaming
      await api.sendMessageStream(currentConversationId, content, (eventType, event) => {
        switch (eventType) {
          case 'stage1_start':
            setCurrentConversation((prev) => {
              const messages = [...prev.messages];
              const lastMsg = messages[messages.length - 1];
              lastMsg.loading.stage1 = true;
              return { ...prev, messages };
            });
            break;

          case 'stage1_complete':
            setCurrentConversation((prev) => {
              const messages = [...prev.messages];
              const lastMsg = messages[messages.length - 1];
              lastMsg.stage1 = event.data;
              lastMsg.loading.stage1 = false;
              return { ...prev, messages };
            });
            break;

          case 'stage2_start':
            setCurrentConversation((prev) => {
              const messages = [...prev.messages];
              const lastMsg = messages[messages.length - 1];
              lastMsg.loading.stage2 = true;
              return { ...prev, messages };
            });
            break;

          case 'stage2_complete':
            setCurrentConversation((prev) => {
              const messages = [...prev.messages];
              const lastMsg = messages[messages.length - 1];
              lastMsg.stage2 = event.data;
              lastMsg.metadata = event.metadata;
              lastMsg.loading.stage2 = false;
              return { ...prev, messages };
            });
            break;

          case 'stage3_start':
            setCurrentConversation((prev) => {
              const messages = [...prev.messages];
              const lastMsg = messages[messages.length - 1];
              lastMsg.loading.stage3 = true;
              return { ...prev, messages };
            });
            break;

          case 'stage3_complete':
            setCurrentConversation((prev) => {
              const messages = [...prev.messages];
              const lastMsg = messages[messages.length - 1];
              lastMsg.stage3 = event.data;
              lastMsg.loading.stage3 = false;
              return { ...prev, messages };
            });
            break;

          case 'title_complete':
            // Reload conversations to get updated title
            loadConversations();
            break;

          case 'complete':
            // Stream complete, reload conversations list
            loadConversations();
            setIsLoading(false);
            break;

          case 'error':
            console.error('Stream error:', event.message);
            setIsLoading(false);
            break;

          default:
            console.log('Unknown event type:', eventType);
        }
      });
    } catch (error) {
      console.error('Failed to send message:', error);
      // Remove optimistic messages on error
      setCurrentConversation((prev) => ({
        ...prev,
        messages: prev.messages.slice(0, -2),
      }));
      setIsLoading(false);
    }
  };

  return (
    <div className="app">
      <Sidebar
        conversations={conversations}
        currentConversationId={currentConversationId}
        onSelectConversation={handleSelectConversation}
        onNewConversation={handleNewConversation}
        onOpenSettings={() => setViewMode('settings')}
      />

      {viewMode === 'settings' ? (
        <ConfigurationPortal
          configuration={configuration}
          discoveredModels={discoveredModels}
          isLoading={isConfigLoading}
          isSaving={isConfigSaving}
          onConfigurationChange={setConfiguration}
          onSave={handleSaveConfiguration}
          onRefresh={loadConfiguration}
          onClose={() => setViewMode('chat')}
        />
      ) : (
        <ChatInterface
          conversation={currentConversation}
          onSendMessage={handleSendMessage}
          isLoading={isLoading}
        />
      )}
    </div>
  );
}

export default App;
